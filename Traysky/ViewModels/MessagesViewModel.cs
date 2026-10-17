using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky.Chat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The Messages page: every conversation the account is in, newest activity first, requests
/// included (and labelled) so nothing waits unseen.
/// </summary>
public sealed partial class MessagesViewModel : ObservableObject
{
    private static readonly Lazy<MessagesViewModel> _instance = new(() => new MessagesViewModel());

    public static MessagesViewModel Instance => _instance.Value;

    private const int PageSize = 30;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cursor;
    private DateTimeOffset _loadedAtUtc = DateTimeOffset.MinValue;

    private MessagesViewModel()
    {
        BlueskySessionService.Instance.SignedOut += (_, _) => Clear();
    }

    public ObservableCollection<ConversationItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public Task RefreshIfStaleAsync()
    {
        if (Items.Count > 0 && !ChatService.Instance.HasUnread && DateTimeOffset.UtcNow - _loadedAtUtc < StaleAfter)
            return Task.CompletedTask;

        return RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        BlueskySessionService session = BlueskySessionService.Instance;
        if (!session.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoading = Items.Count == 0;
        Error = null;

        try
        {
            if (PreviewMode.IsEnabled)
            {
                Replace(PreviewMode.SampleConversations());
                _cursor = null;
                return;
            }

            await session.EnsureAuthenticatedAsync();

            AtProtoHttpResult<Conversations> result = await session.Agent.ListConversations(limit: PageSize);
            if (!result.Succeeded || result.Result is null)
            {
                Error = result.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Your session expired. Sign in again.",
                    0 => "Couldn't reach Bluesky. Check your connection.",
                    _ => "Couldn't load your messages."
                };
                LogService.Warn("Chat", $"Conversation list failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return;
            }

            Replace(result.Result.Select(c => ChatMapper.ToConversationItem(c, session.Did)));
            _cursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Error("Chat", "Conversation list threw", ex);
            Error = "Couldn't load your messages.";
        }
        finally
        {
            IsLoading = false;
            _gate.Release();
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        BlueskySessionService session = BlueskySessionService.Instance;
        if (!HasMore || !session.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoadingMore = true;
        try
        {
            AtProtoHttpResult<Conversations> result = await session.Agent.ListConversations(limit: PageSize, cursor: _cursor);
            if (!result.Succeeded || result.Result is null)
                return;

            HashSet<string> known = Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (ConversationView view in result.Result)
            {
                if (known.Add(view.Id))
                    Items.Add(ChatMapper.ToConversationItem(view, session.Did));
            }
            _cursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Error("Chat", "Conversation list load more threw", ex);
        }
        finally
        {
            IsLoadingMore = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Moves a conversation to the top after a message was sent from it, or adds it when it
    /// was just started from a profile and isn't in the list yet.
    /// </summary>
    public void BringToTop(ConversationItem conversation)
    {
        ConversationItem? existing = Items.FirstOrDefault(i => i.Id == conversation.Id);
        if (existing is not null)
        {
            int index = Items.IndexOf(existing);
            if (!ReferenceEquals(existing, conversation))
            {
                existing.LastMessagePreview = conversation.LastMessagePreview;
                existing.LastMessageAt = conversation.LastMessageAt;
                existing.UnreadCount = conversation.UnreadCount;
                existing.IsRequest = conversation.IsRequest;
            }
            if (index > 0)
                Items.Move(index, 0);
            return;
        }

        Items.Insert(0, conversation);
        IsEmpty = false;
    }

    /// <summary>Something changed in chat that the loaded list doesn't show; the next visit reloads it.</summary>
    public void Invalidate() => _loadedAtUtc = DateTimeOffset.MinValue;

    /// <summary>Drops a conversation that was left or declined.</summary>
    public void Remove(string conversationId)
    {
        ConversationItem? item = Items.FirstOrDefault(i => i.Id == conversationId);
        if (item is null)
            return;

        ChatService.Instance.ConversationRead(item);
        Items.Remove(item);
        IsEmpty = Items.Count == 0;
    }

    /// <summary>Accepts a request without opening it. Shows at once, undone if the server refuses.</summary>
    [RelayCommand]
    private async Task AcceptRequestAsync(ConversationItem? conversation)
    {
        if (conversation is null || !conversation.IsRequest)
            return;

        conversation.IsRequest = false;
        ChatService.Instance.UpdateUnreadFrom(Items);
        if (PreviewMode.IsEnabled)
            return;

        if (!await ChatCall(async agent =>
            {
                var r = await agent.AcceptConversation(conversation.Id);
                return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
            }, "Accept"))
        {
            conversation.IsRequest = true;
            ChatService.Instance.UpdateUnreadFrom(Items);
        }
    }

    /// <summary>Declines a request: it leaves the list at once and comes back if the server refuses.</summary>
    [RelayCommand]
    private async Task DeclineRequestAsync(ConversationItem? conversation)
    {
        if (conversation is null || !conversation.IsRequest)
            return;

        int index = Items.IndexOf(conversation);
        if (index < 0)
            return;

        Items.RemoveAt(index);
        IsEmpty = Items.Count == 0;
        if (PreviewMode.IsEnabled)
            return;

        if (!await ChatCall(async agent =>
            {
                var r = await agent.LeaveConversation(conversation.Id);
                return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
            }, "Decline"))
        {
            Items.Insert(Math.Min(index, Items.Count), conversation);
            IsEmpty = false;
        }
    }

    private async Task<bool> ChatCall(Func<idunno.Bluesky.BlueskyAgent, Task<(bool Ok, System.Net.HttpStatusCode Status, AtErrorDetail? Detail)>> call, string what)
    {
        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            await session.EnsureAuthenticatedAsync();
            (bool ok, System.Net.HttpStatusCode status, AtErrorDetail? detail) = await call(session.Agent);
            if (ok)
                return true;

            Error = status == 0 ? "Couldn't reach Bluesky. Nothing was changed." : $"{what} didn't go through.";
            LogService.Warn("Chat", $"{what} failed: {(int)status} {detail?.Error}");
        }
        catch (Exception ex)
        {
            Error = $"{what} didn't go through.";
            LogService.Warn("Chat", $"{what} threw: {ex.Message}");
        }
        return false;
    }

    private void Replace(IEnumerable<ConversationItem> conversations)
    {
        Items.Clear();
        foreach (ConversationItem c in conversations)
            Items.Add(c);

        _loadedAtUtc = DateTimeOffset.UtcNow;
        IsEmpty = Items.Count == 0;
        ChatService.Instance.UpdateUnreadFrom(Items);
    }

    private void Clear()
    {
        Items.Clear();
        _cursor = null;
        _loadedAtUtc = DateTimeOffset.MinValue;
        Error = null;
        IsEmpty = false;
    }
}

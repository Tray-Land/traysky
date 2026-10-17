using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.Bluesky.Chat.Notifications;
using idunno.Bluesky.Notifications.PreferenceTypes;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;
using ChatPreference = idunno.Bluesky.Chat.Notifications.Preference;
using ChatPreferences = idunno.Bluesky.Chat.Notifications.Preferences;
using ServerPreferences = idunno.Bluesky.Notifications.Preferences;

namespace Traysky.ViewModels;

/// <summary>
/// Bluesky's own per-type notification settings (what shows in the list, what gets pushed, and
/// whether it is limited to people you follow). Each toggle saves straight away. The save sends
/// back the preferences record that was loaded with only these types changed, so chat settings
/// and anything the service adds later survive; a refused save puts every toggle back.
/// </summary>
public sealed partial class NotificationPreferencesViewModel : ObservableObject
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private ServerPreferences? _current;

    public NotificationPreferencesViewModel()
    {
        foreach (NotificationPrefRow row in NotificationPreferencePolicy.Rows)
        {
            var item = new NotificationPrefItem(row);
            item.Edited += OnItemEdited;
            Items.Add(item);
        }

        foreach (ChatPrefRow row in ChatNotificationPreferencePolicy.Rows)
        {
            var item = new ChatPrefItem(row);
            item.Edited += OnChatItemEdited;
            ChatItems.Add(item);
        }
    }

    public ObservableCollection<NotificationPrefItem> Items { get; } = [];

    /// <summary>Messages and message requests: they live in a separate record from the other types.</summary>
    public ObservableCollection<ChatPrefItem> ChatItems { get; } = [];

    /// <summary>False when the session has no direct message access, so there is nothing to show.</summary>
    [ObservableProperty]
    public partial bool IsChatReady { get; private set; }

    private ChatPreferences? _currentChat;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>The toggles are only usable once the current settings have been read.</summary>
    [ObservableProperty]
    public partial bool IsReady { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public async Task LoadAsync()
    {
        Error = null;

        if (PreviewMode.IsEnabled)
        {
            foreach (NotificationPrefItem item in Items)
                item.Set(showInList: true, push: item.Row.Type != NotificationPrefType.Like, followsOnly: item.Row.Type == NotificationPrefType.Follow);
            foreach (ChatPrefItem chat in ChatItems)
                chat.Set(ChatNotificationPreferencePolicy.DefaultPush, chat.Row.Type == ChatPrefType.ChatRequest);
            IsReady = true;
            IsChatReady = true;
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        IsLoading = true;
        try
        {
            var result = await BlueskySessionService.Instance.Agent.GetNotificationPreferences();
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("NotifPrefs", $"Get failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = result.StatusCode == 0 ? "Couldn't reach Bluesky. Check your connection." : "Couldn't load your notification settings.";
                return;
            }

            _current = result.Result;
            ApplyToItems(_current);
            IsReady = true;

            await LoadChatAsync();
        }
        catch (Exception ex)
        {
            LogService.Warn("NotifPrefs", $"Get threw: {ex.Message}");
            Error = "Couldn't load your notification settings.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Chat preferences are best effort: a session without DM access can't read them, and that is
    /// not an error on this screen, the section just stays hidden.
    /// </summary>
    private async Task LoadChatAsync()
    {
        if (!ChatService.Instance.CanUseChat)
            return;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.GetChatNotificationPreferences();
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("NotifPrefs", $"Chat get failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return;
            }

            _currentChat = result.Result;
            ApplyToChatItems(_currentChat);
            IsChatReady = true;
        }
        catch (Exception ex)
        {
            LogService.Warn("NotifPrefs", $"Chat get threw: {ex.Message}");
        }
    }

    private void OnChatItemEdited(ChatPrefItem item) => _ = SaveChatAsync(item);

    private async Task SaveChatAsync(ChatPrefItem edited)
    {
        if (PreviewMode.IsEnabled || _currentChat is null)
            return;

        await _saveGate.WaitAsync();
        try
        {
            Error = null;
            ChatPreferences next = new(ToChatPreference(ChatItems[0]), ToChatPreference(ChatItems[1]));

            var result = await BlueskySessionService.Instance.Agent.PutChatNotificationPreferences(next, CancellationToken.None);
            if (result.Succeeded)
            {
                _currentChat = next;
                return;
            }

            LogService.Warn("NotifPrefs", $"Chat put failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("NotifPrefs", $"Chat put threw: {ex.Message}");
        }
        finally
        {
            _saveGate.Release();
        }

        ApplyToChatItems(_currentChat);
        Error = $"Couldn't save the {edited.Label.ToLowerInvariant()} setting. It was put back.";
    }

    private void ApplyToChatItems(ChatPreferences prefs)
    {
        ChatItems[0].Set(ReadChat(prefs.Chat).Push, ReadChat(prefs.Chat).FollowsOnly);
        ChatItems[1].Set(ReadChat(prefs.ChatRequest).Push, ReadChat(prefs.ChatRequest).FollowsOnly);
    }

    // A preference the server did not send reads as Bluesky's default.
    private static (bool Push, bool FollowsOnly) ReadChat(ChatPreference? p) =>
        p is null
            ? (ChatNotificationPreferencePolicy.DefaultPush, ChatNotificationPreferencePolicy.DefaultFollowsOnly)
            : (p.Push, string.Equals(p.Include, ChatPreferencesKnownValues.Follows, StringComparison.Ordinal));

    private static ChatPreference ToChatPreference(ChatPrefItem i) =>
        new(i.FollowsOnly ? ChatPreferencesKnownValues.Follows : ChatPreferencesKnownValues.All, i.Push);

    private void OnItemEdited(NotificationPrefItem item) => _ = SaveAsync(item);

    private async Task SaveAsync(NotificationPrefItem edited)
    {
        if (PreviewMode.IsEnabled || _current is null)
            return;

        await _saveGate.WaitAsync();
        try
        {
            Error = null;
            ServerPreferences next = Build(_current);

            var result = await BlueskySessionService.Instance.Agent.PutNotificationPreferences(next);
            if (result.Succeeded)
            {
                _current = next;
                return;
            }

            LogService.Warn("NotifPrefs", $"Put failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("NotifPrefs", $"Put threw: {ex.Message}");
        }
        finally
        {
            _saveGate.Release();
        }

        // Show what the server still has rather than leave a toggle that did nothing.
        ApplyToItems(_current);
        Error = $"Couldn't save the {edited.Label.ToLowerInvariant()} setting. It was put back.";
    }

    private void ApplyToItems(ServerPreferences prefs)
    {
        foreach (NotificationPrefItem item in Items)
        {
            (bool list, bool push, bool followsOnly) = Read(prefs, item.Row.Type);
            item.Set(list, push, followsOnly);
        }
    }

    private ServerPreferences Build(ServerPreferences from)
    {
        ServerPreferences next = from;
        foreach (NotificationPrefItem i in Items)
            next = Write(next, i);
        return next;
    }

    private static (bool List, bool Push, bool FollowsOnly) Read(ServerPreferences p, NotificationPrefType type) => type switch
    {
        NotificationPrefType.Like => Filterable(p.Like),
        NotificationPrefType.LikeViaRepost => Filterable(p.LikeViaRepost),
        NotificationPrefType.Repost => Filterable(p.Repost),
        NotificationPrefType.RepostViaRepost => Filterable(p.RepostViaRepost),
        NotificationPrefType.Follow => Filterable(p.Follow),
        NotificationPrefType.Reply => Filterable(p.Reply),
        NotificationPrefType.Mention => Filterable(p.Mention),
        NotificationPrefType.Quote => Filterable(p.Quote),
        NotificationPrefType.StarterPackJoined => Plain(p.StarterPackJoined),
        NotificationPrefType.SubscribedPost => Plain(p.SubscribedPost),
        NotificationPrefType.Verified => Plain(p.Verified),
        _ => (true, true, false)
    };

    private static ServerPreferences Write(ServerPreferences p, NotificationPrefItem i) => i.Row.Type switch
    {
        NotificationPrefType.Like => p with { Like = ToFilterable(i) },
        NotificationPrefType.LikeViaRepost => p with { LikeViaRepost = ToFilterable(i) },
        NotificationPrefType.Repost => p with { Repost = ToFilterable(i) },
        NotificationPrefType.RepostViaRepost => p with { RepostViaRepost = ToFilterable(i) },
        NotificationPrefType.Follow => p with { Follow = ToFilterable(i) },
        NotificationPrefType.Reply => p with { Reply = ToFilterable(i) },
        NotificationPrefType.Mention => p with { Mention = ToFilterable(i) },
        NotificationPrefType.Quote => p with { Quote = ToFilterable(i) },
        NotificationPrefType.StarterPackJoined => p with { StarterPackJoined = ToPlain(i) },
        NotificationPrefType.SubscribedPost => p with { SubscribedPost = ToPlain(i) },
        NotificationPrefType.Verified => p with { Verified = ToPlain(i) },
        _ => p
    };

    // A type the server did not send reads as fully on, which is Bluesky's default.
    private static (bool, bool, bool) Filterable(FilterablePreference? f) =>
        f is null ? (true, true, false) : (f.List, f.Push, f.Include == LimitTo.Follows);

    private static (bool, bool, bool) Plain(NonFilterablePreference? n) =>
        n is null ? (true, true, false) : (n.List, n.Push, false);

    private static FilterablePreference ToFilterable(NotificationPrefItem i) =>
        new(i.FollowsOnly ? LimitTo.Follows : LimitTo.All, i.ShowInList, i.Push);

    private static NonFilterablePreference ToPlain(NotificationPrefItem i) => new(i.ShowInList, i.Push);

    [RelayCommand]
    private Task Retry() => LoadAsync();
}

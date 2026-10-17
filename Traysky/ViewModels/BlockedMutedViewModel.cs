using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.Bluesky.Actor;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The accounts the signed-in user has blocked and muted. Both lists load when the page opens and
/// page in independently; unblock / unmute removes the row straight away and puts it back if the
/// server refuses.
/// </summary>
public sealed partial class BlockedMutedViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SemaphoreSlim _blocksGate = new(1, 1);
    private readonly SemaphoreSlim _mutesGate = new(1, 1);
    private readonly SemaphoreSlim _blockedListsGate = new(1, 1);
    private readonly SemaphoreSlim _mutedListsGate = new(1, 1);
    private string? _blocksCursor;
    private string? _mutesCursor;
    private string? _blockedListsCursor;
    private string? _mutedListsCursor;

    public ObservableCollection<ActorItem> Blocked { get; } = [];
    public ObservableCollection<ActorItem> Muted { get; } = [];
    public ObservableCollection<ListItem> BlockedLists { get; } = [];
    public ObservableCollection<ListItem> MutedLists { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMoreBlocked => !string.IsNullOrEmpty(_blocksCursor);
    public bool HasMoreMuted => !string.IsNullOrEmpty(_mutesCursor);
    public bool HasMoreBlockedLists => !string.IsNullOrEmpty(_blockedListsCursor);
    public bool HasMoreMutedLists => !string.IsNullOrEmpty(_mutedListsCursor);

    public async Task LoadAsync()
    {
        Error = null;
        Blocked.Clear();
        Muted.Clear();
        BlockedLists.Clear();
        MutedLists.Clear();
        _blocksCursor = null;
        _mutesCursor = null;
        _blockedListsCursor = null;
        _mutedListsCursor = null;

        if (PreviewMode.IsEnabled)
        {
            foreach (ActorItem item in PreviewMode.SampleManagedAccounts(blocks: true))
                Blocked.Add(item);
            foreach (ActorItem item in PreviewMode.SampleManagedAccounts(blocks: false))
                Muted.Add(item);
            foreach (ListItem list in PreviewMode.SampleLists().Where(l => l.Kind == ListKind.Moderation))
            {
                list.ManagedActionLabel = ManagedAccountsPolicy.ActionLabel(list.IsBlocked);
                (list.IsBlocked ? BlockedLists : MutedLists).Add(list);
            }
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        IsLoading = true;
        try
        {
            await Task.WhenAll(
                LoadBlockedAsync(reset: true),
                LoadMutedAsync(reset: true),
                LoadListsAsync(blocks: true, reset: true),
                LoadListsAsync(blocks: false, reset: true));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task LoadMoreBlockedListsAsync() => HasMoreBlockedLists ? LoadListsAsync(blocks: true, reset: false) : Task.CompletedTask;

    public Task LoadMoreMutedListsAsync() => HasMoreMutedLists ? LoadListsAsync(blocks: false, reset: false) : Task.CompletedTask;

    private async Task LoadListsAsync(bool blocks, bool reset)
    {
        SemaphoreSlim gate = blocks ? _blockedListsGate : _mutedListsGate;
        if (!await gate.WaitAsync(0))
            return;

        try
        {
            ManagedListsPage page = await ListsService.GetManagedListsAsync(blocks, reset ? null : (blocks ? _blockedListsCursor : _mutedListsCursor), PageSize);
            if (page.Items is null)
            {
                Error = LoadError(page.Status);
                return;
            }

            ObservableCollection<ListItem> target = blocks ? BlockedLists : MutedLists;
            if (reset)
                target.Clear();

            foreach (ListItem list in page.Items)
            {
                list.ManagedActionLabel = ManagedAccountsPolicy.ActionLabel(blocks);
                target.Add(list);
            }

            if (blocks)
                _blockedListsCursor = page.Cursor;
            else
                _mutedListsCursor = page.Cursor;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Unblocks or unmutes a whole list. The row leaves immediately; if the server refuses it returns
    /// to its old position and <see cref="Error"/> says so.
    /// </summary>
    public async Task RemoveListAsync(ListItem list, bool blocks)
    {
        ObservableCollection<ListItem> items = blocks ? BlockedLists : MutedLists;
        int index = items.IndexOf(list);
        if (index < 0)
            return;

        Error = null;
        items.RemoveAt(index);

        bool ok = PreviewMode.IsEnabled
            || await (blocks ? ListsService.SetListBlockedAsync(list.Uri, false) : ListsService.SetListMutedAsync(list.Uri, false, list.Kind));
        if (ok)
            return;

        items.Insert(ManagedAccountsPolicy.RestoreIndex(index, items.Count), list);
        Error = ManagedAccountsPolicy.ListFailureText(blocks, list.Name);
    }

    public Task LoadMoreBlockedAsync() => HasMoreBlocked ? LoadBlockedAsync(reset: false) : Task.CompletedTask;

    public Task LoadMoreMutedAsync() => HasMoreMuted ? LoadMutedAsync(reset: false) : Task.CompletedTask;

    private async Task LoadBlockedAsync(bool reset)
    {
        if (!await _blocksGate.WaitAsync(0))
            return;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.GetBlocks(limit: PageSize, cursor: reset ? null : _blocksCursor);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("BlockedMuted", $"GetBlocks failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = LoadError(result.StatusCode);
                return;
            }

            if (reset)
                Blocked.Clear();

            foreach (ProfileView profile in result.Result)
                Blocked.Add(FeedMapper.ToManagedActorItem(profile, blocks: true));

            _blocksCursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("BlockedMuted", $"GetBlocks threw: {ex.Message}");
            Error = "Couldn't load blocked accounts.";
        }
        finally
        {
            _blocksGate.Release();
        }
    }

    private async Task LoadMutedAsync(bool reset)
    {
        if (!await _mutesGate.WaitAsync(0))
            return;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.GetMutes(limit: PageSize, cursor: reset ? null : _mutesCursor);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("BlockedMuted", $"GetMutes failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = LoadError(result.StatusCode);
                return;
            }

            if (reset)
                Muted.Clear();

            foreach (ProfileView profile in result.Result)
                Muted.Add(FeedMapper.ToManagedActorItem(profile, blocks: false));

            _mutesCursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("BlockedMuted", $"GetMutes threw: {ex.Message}");
            Error = "Couldn't load muted accounts.";
        }
        finally
        {
            _mutesGate.Release();
        }
    }

    /// <summary>
    /// Unblocks or unmutes <paramref name="item"/>. The row leaves the list immediately; if the
    /// server refuses it returns to its old position and <see cref="Error"/> says so.
    /// </summary>
    public async Task RemoveAsync(ActorItem item, bool blocks)
    {
        ObservableCollection<ActorItem> list = blocks ? Blocked : Muted;
        int index = list.IndexOf(item);
        if (index < 0)
            return;

        Error = null;
        list.RemoveAt(index);

        bool ok;
        if (PreviewMode.IsEnabled)
            ok = true;
        else if (blocks)
            ok = item.BlockUri is not null && await ModerationService.UnblockAsync(item.BlockUri);
        else
            ok = await ModerationService.SetMutedAsync(item.Did, muted: false);

        if (ok)
            return;

        list.Insert(ManagedAccountsPolicy.RestoreIndex(index, list.Count), item);
        Error = ManagedAccountsPolicy.FailureText(blocks, item.Handle);
    }

    private static string LoadError(System.Net.HttpStatusCode status) => status == 0
        ? "Couldn't reach Bluesky. Check your connection."
        : "Couldn't load this list.";

    [RelayCommand]
    private Task Retry() => LoadAsync();
}

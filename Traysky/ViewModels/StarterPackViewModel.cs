using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky.Graph;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>The accounts in one starter pack, paged from the pack's underlying list, plus "Follow all".</summary>
public sealed partial class StarterPackViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<PackMember> _members = [];
    private StarterPackArgs? _args;
    private string? _cursor;

    public ObservableCollection<ActorItem> Members { get; } = [];

    /// <summary>Display names of the feeds bundled with the pack.</summary>
    public ObservableCollection<string> Feeds { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    public partial string Description { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ByLine { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasLoaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFollowAll))]
    [NotifyPropertyChangedFor(nameof(FollowAllLabel))]
    public partial int FollowTargetCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFollowAll))]
    public partial bool IsFollowingAll { get; private set; }

    [ObservableProperty]
    public partial int FollowDone { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFollowTotalUnknown))]
    public partial int FollowTotal { get; private set; }

    [ObservableProperty]
    public partial string FollowProgress { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFollowSummary))]
    public partial string FollowSummary { get; private set; } = string.Empty;

    public bool HasDescription => Description.Length > 0;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsEmpty => HasLoaded && Members.Count == 0 && !HasError;

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public bool HasFeeds => Feeds.Count > 0;

    public bool HasFollowSummary => FollowSummary.Length > 0;

    /// <summary>True while the rest of the pack is still being fetched, before the number to follow is known.</summary>
    public bool IsFollowTotalUnknown => FollowTotal == 0;

    /// <summary>The button is offered while there is someone left to follow (the count covers the pages loaded so far; the rest are fetched on click).</summary>
    public bool ShowFollowAll => !IsFollowingAll && (FollowTargetCount > 0 || HasMore) && CanFollow;

    public string FollowAllLabel => StarterPackPolicy.FollowAllLabel(FollowTargetCount);

    private static bool CanFollow => !PreviewMode.IsEnabled && BlueskySessionService.Instance.IsSignedIn;

    public async Task LoadAsync(StarterPackArgs args)
    {
        _args = args;
        Name = args.Name;
        Description = args.Description;
        ByLine = args.CreatorHandle.Length > 0 ? $"by @{args.CreatorHandle}" : string.Empty;
        Members.Clear();
        _members.Clear();
        Feeds.Clear();
        OnPropertyChanged(nameof(HasFeeds));
        _cursor = null;
        Error = null;
        HasLoaded = false;
        FollowSummary = string.Empty;
        FollowTargetCount = 0;

        if (PreviewMode.IsEnabled)
        {
            foreach (ActorItem actor in PreviewMode.SampleSearchActors())
                Members.Add(actor);
            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
        {
            HasLoaded = true;
            return;
        }

        if (args.AtUri.Length > 0)
        {
            IsLoading = true;
            StarterPackDetail? detail = await StarterPackService.GetAsync(args.AtUri);
            IsLoading = false;

            if (detail is not null)
            {
                StarterPackItem pack = detail.Pack;
                _args = args with
                {
                    ListUri = pack.ListUri,
                    Name = pack.Name,
                    Description = pack.Description,
                    CreatorHandle = pack.CreatorHandle,
                    WebUrl = pack.WebUrl
                };
                Name = pack.Name;
                Description = pack.Description;
                ByLine = pack.CreatorHandle.Length > 0 ? $"by @{pack.CreatorHandle}" : string.Empty;

                foreach (string feed in detail.FeedNames)
                    Feeds.Add(feed);
                OnPropertyChanged(nameof(HasFeeds));
            }
        }

        if (_args.ListUri.Length == 0)
        {
            Error = "Couldn't load this starter pack.";
            HasLoaded = true;
            return;
        }

        await LoadPageAsync(reset: true);
    }

    public Task LoadMoreAsync() => HasMore ? LoadPageAsync(reset: false) : Task.CompletedTask;

    /// <summary>Loads one page of members. False when the page couldn't be loaded (or another load held the gate and <paramref name="wait"/> was off).</summary>
    private async Task<bool> LoadPageAsync(bool reset, bool wait = false)
    {
        if (_args is null)
            return false;

        if (wait)
            await _gate.WaitAsync();
        else if (!await _gate.WaitAsync(0))
            return false;

        IsLoading = true;
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            AtProtoHttpResult<ListViewWithItems> result = await BlueskySessionService.Instance.Agent.GetList(
                new AtUri(_args.ListUri),
                limit: PageSize,
                cursor: reset ? null : _cursor,
                subscribedLabelers: LabelService.Instance.Subscribed);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("StarterPack", $"GetList failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                if (reset)
                {
                    Error = result.StatusCode == 0
                        ? "Couldn't reach Bluesky. Check your connection."
                        : "Couldn't load this starter pack.";
                }
                return false;
            }

            foreach (ListItemView item in result.Result)
            {
                Members.Add(FeedMapper.ToActorItem(item.Subject));
                _members.Add(new PackMember(
                    item.Subject.Did.ToString(),
                    IsFollowing: item.Subject.Viewer?.Following is not null,
                    IsBlocking: item.Subject.Viewer?.Blocking is not null,
                    IsBlockedBy: item.Subject.Viewer?.BlockedBy == true));
            }

            _cursor = result.Result.Cursor;
            return true;
        }
        catch (Exception ex)
        {
            LogService.Warn("StarterPack", $"GetList threw: {ex.Message}");
            if (reset)
                Error = "Couldn't load this starter pack.";
            return false;
        }
        finally
        {
            IsLoading = false;
            HasLoaded = true;
            RefreshFollowTargets();
            OnPropertyChanged(nameof(IsEmpty));
            _gate.Release();
        }
    }

    private void RefreshFollowTargets()
    {
        FollowTargetCount = StarterPackPolicy.FollowTargets(_members, BlueskySessionService.Instance.Did).Count;
        OnPropertyChanged(nameof(ShowFollowAll));
    }

    /// <summary>
    /// Follows everyone in the pack you don't already follow, in small batches. Pages that haven't been
    /// scrolled to yet are fetched first. Cancelling stops between accounts; those already followed stay followed.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task FollowAllAsync(CancellationToken cancellationToken)
    {
        if (IsFollowingAll || _args is null || !CanFollow)
            return;

        IsFollowingAll = true;
        FollowSummary = string.Empty;
        FollowDone = 0;
        FollowTotal = 0;
        FollowProgress = "Loading the full pack…";

        int followed = 0, failed = 0, total = 0;
        bool cancelled = false, rateLimited = false;

        try
        {
            while (HasMore)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await LoadPageAsync(reset: false, wait: true))
                {
                    FollowSummary = "Couldn't load the whole pack. Try again.";
                    return;
                }
            }

            IReadOnlyList<string> targets = StarterPackPolicy.FollowTargets(_members, BlueskySessionService.Instance.Did);
            total = targets.Count;
            FollowTotal = total;

            IReadOnlyList<IReadOnlyList<string>> batches = StarterPackPolicy.Chunk(targets, StarterPackPolicy.FollowBatchSize);
            for (int b = 0; b < batches.Count && !rateLimited; b++)
            {
                foreach (string did in batches[b])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FollowProgress = StarterPackPolicy.ProgressText(followed + failed, total);

                    // Not cancellable mid-request: a follow that was sent is counted and kept.
                    AtProtoHttpResult<CreateRecordResult> result = await BlueskySessionService.Instance.Agent.Follow(new Did(did));
                    if (result.Succeeded)
                    {
                        followed++;
                        MarkFollowing(did);
                    }
                    else if (StarterPackPolicy.IsRateLimited((int)result.StatusCode))
                    {
                        LogService.Warn("StarterPack", "Follow all stopped: rate limited");
                        rateLimited = true;
                        break;
                    }
                    else
                    {
                        failed++;
                        LogService.Warn("StarterPack", $"Follow failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    }

                    FollowDone = followed + failed;
                }

                if (b < batches.Count - 1 && !rateLimited)
                    await Task.Delay(StarterPackPolicy.FollowBatchDelayMilliseconds, cancellationToken);
            }

            FollowSummary = StarterPackPolicy.SummaryText(followed, failed, total, cancelled: false, rateLimited);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            FollowSummary = StarterPackPolicy.SummaryText(followed, failed, total, cancelled, rateLimited: false);
        }
        catch (Exception ex)
        {
            LogService.Warn("StarterPack", $"Follow all threw: {ex.Message}");
            FollowSummary = StarterPackPolicy.SummaryText(followed, failed + 1, total, cancelled: false, rateLimited: false);
        }
        finally
        {
            IsFollowingAll = false;
            FollowProgress = string.Empty;
            RefreshFollowTargets();
        }
    }

    private void MarkFollowing(string did)
    {
        int index = _members.FindIndex(m => m.Did == did);
        if (index >= 0)
            _members[index] = _members[index] with { IsFollowing = true };
    }

    [RelayCommand]
    private Task Retry() => _args is null ? Task.CompletedTask : LoadAsync(_args);

    [RelayCommand]
    private Task OpenInBrowser() => _args is null || _args.WebUrl.Length == 0
        ? Task.CompletedTask
        : RichTextBuilder.OpenAsync(_args.WebUrl);
}

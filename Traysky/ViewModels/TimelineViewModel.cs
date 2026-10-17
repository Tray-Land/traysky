using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Feed;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The timeline page's content: whichever feed is currently selected (the home timeline by
/// default, or one of the account's other pinned feeds - see <see cref="SelectFeed"/>), paged
/// by cursor. One instance for the app's life so reopening the flyout is instant; it refreshes
/// itself when its content is older than <see cref="StaleAfter"/>.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    private static readonly Lazy<TimelineViewModel> _instance = new(() => new TimelineViewModel());

    public static TimelineViewModel Instance => _instance.Value;

    // Enough to fill the flyout a couple of times over; scrolling fetches the rest. Kept small
    // because this view model lives for the app's life and holds every post it has loaded.
    private const int PageSize = 15;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    // createRecord returns once the PDS has the post, but the AppView serving getTimeline picks
    // it up from the firehose a moment later - fetch straight away and the new post is missing.
    private static readonly TimeSpan PostIndexDelay = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cursor;
    private DateTimeOffset _loadedAtUtc = DateTimeOffset.MinValue;

    /// <summary>The feed currently shown; null means the account's home timeline ("Following").</summary>
    private FeedTabItem? _currentFeed;

    private TimelineViewModel()
    {
        BlueskySessionService.Instance.SignedOut += (_, _) => Clear();
        ComposeViewModel.Instance.Posted += (_, _) => _ = RefreshAfterPostAsync();
    }

    /// <summary>
    /// Marks the list stale first, so returning to the timeline (or reopening the popup) refreshes
    /// even if this one is dropped by the gate or lands before the post is indexed; then refreshes
    /// once the AppView has had a moment to see the new post. Posted is raised on the UI thread,
    /// so the await resumes there too.
    /// </summary>
    private async Task RefreshAfterPostAsync()
    {
        _loadedAtUtc = DateTimeOffset.MinValue;
        await Task.Delay(PostIndexDelay);
        await RefreshAsync();
    }

    /// <summary>
    /// What the list shows, top to bottom: <see cref="PostItem"/>s (a followed user's post, or the
    /// post it replies to when reply context is on) and <see cref="ThreadGapItem"/> "Load more" rows.
    /// </summary>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>Whether the rows now loaded were built with reply context; a settings change then needs a reload.</summary>
    private bool _builtWithContext = true;

    /// <summary>The feed tab currently shown, or null for the home timeline.</summary>
    public FeedTabItem? CurrentFeed => _currentFeed;

    /// <summary>Switches which feed is shown and reloads it from the start. A no-op if it's already selected.</summary>
    public void SelectFeed(FeedTabItem? feed)
    {
        string newKey = feed?.Key ?? "timeline";
        if (newKey == (_currentFeed?.Key ?? "timeline"))
            return;

        _currentFeed = feed;
        Clear();
        _ = RefreshAsync();
    }

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

    /// <summary>Called when the flyout opens: refresh only if the list is empty or old.</summary>
    public Task RefreshIfStaleAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return Task.CompletedTask;

        if (Rows.Count > 0
            && DateTimeOffset.UtcNow - _loadedAtUtc < StaleAfter
            && _builtWithContext == SettingsService.ShowReplyContext)
        {
            foreach (object row in Rows)
            {
                if (row is PostItem p)
                    p.RefreshTimeAgo();
            }
            return Task.CompletedTask;
        }

        return RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoading = Rows.Count == 0;
        Error = null;

        try
        {
            bool includeContext = SettingsService.ShowReplyContext;

            if (PreviewMode.IsEnabled)
            {
                Rows.Clear();
                foreach (object row in PreviewMode.SampleTimelineRows(includeContext))
                    Rows.Add(row);
                _builtWithContext = includeContext;
                _loadedAtUtc = DateTimeOffset.UtcNow;
                IsEmpty = false;
                return;
            }

            FeedPage page = await FetchPageAsync(cursor: null);

            if (!page.Succeeded || !page.HasContent)
            {
                Error = Describe(page.StatusCode, page.ErrorMessage);
                LogService.Warn("Timeline", $"Refresh failed: {(int)page.StatusCode}");
                return;
            }

            List<object> fresh = [];
            AppendPage(page, fresh, [], includeContext);

            Rows.Clear();
            foreach (object row in fresh)
                Rows.Add(row);

            _builtWithContext = includeContext;
            _cursor = page.Cursor;
            _loadedAtUtc = DateTimeOffset.UtcNow;
            IsEmpty = Rows.Count == 0;
        }
        catch (Exception ex)
        {
            LogService.Error("Timeline", "Refresh threw", ex);
            Error = "Couldn't load your timeline.";
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
        if (!HasMore || !BlueskySessionService.Instance.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoadingMore = true;
        try
        {
            FeedPage page = await FetchPageAsync(_cursor);
            if (!page.Succeeded || !page.HasContent)
            {
                LogService.Warn("Timeline", $"Load more failed: {(int)page.StatusCode}");
                return;
            }

            HashSet<string> seen = [];
            foreach (object row in Rows)
            {
                if (row is PostItem p)
                    seen.Add(p.AtUri);
            }

            List<object> more = [];
            AppendPage(page, more, seen, _builtWithContext);
            foreach (object row in more)
                Rows.Add(row);

            _cursor = page.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Error("Timeline", "Load more threw", ex);
        }
        finally
        {
            IsLoadingMore = false;
            _gate.Release();
        }
    }

    [RelayCommand]
    private Task Like(PostItem post) => PostInteractions.ToggleLikeAsync(post);

    [RelayCommand]
    private Task Repost(PostItem post) => PostInteractions.ToggleRepostAsync(post);

    [RelayCommand]
    private void Reply(PostItem post) => ComposeViewModel.Instance.BeginReply(post);

    [RelayCommand]
    private void Quote(PostItem post) => ComposeViewModel.Instance.BeginQuote(post);

    [RelayCommand]
    private Task OpenInBrowser(PostItem post) => RichTextBuilder.OpenAsync(post.WebUrl);

    [RelayCommand]
    private void OpenProfile(PostItem post) => Pages.ProfilePage.Open(post.AuthorDid.Length > 0 ? post.AuthorDid : post.AuthorHandle);

    /// <summary>
    /// Expands a "Load more" gap: up to <see cref="ReplyContextPolicy.MaxInlineExpansion"/> hidden
    /// posts are put in where the gap is; a longer (or unreachable) chain opens the full thread
    /// page for the reply instead.
    /// </summary>
    [RelayCommand]
    private async Task ExpandGap(ThreadGapItem gap)
    {
        if (gap.IsLoading)
            return;

        gap.IsLoading = true;
        try
        {
            List<PostView> hidden = [];
            IReadOnlyList<string>? missing = null;

            if (!PreviewMode.IsEnabled)
            {
                AtProtoHttpResult<PostThread> result = await BlueskySessionService.Instance.Agent.GetPostThread(
                    new AtUri(gap.ParentUri),
                    depth: 0,
                    parentHeight: ReplyContextPolicy.ExpansionParentHeight,
                    subscribedLabelers: LabelService.Instance.Subscribed);

                if (result.Succeeded && result.Result?.Thread is ThreadViewPost thread && thread.Post is not null)
                {
                    List<PostView> chain = FeedMapper.AncestorsOf(thread);
                    chain.Add(thread.Post);

                    missing = ReplyContextPolicy.MissingBetween(chain.ConvertAll(p => p.Uri.ToString()), gap.RootUri, gap.ParentUri);
                    if (missing is not null)
                    {
                        HashSet<string> wanted = [.. missing];
                        hidden = chain.FindAll(p => wanted.Contains(p.Uri.ToString()));
                    }
                }
                else
                {
                    LogService.Warn("Timeline", $"Expanding a thread gap failed: {(int)result.StatusCode}");
                }
            }

            if (!ReplyContextPolicy.ExpandsInPlace(missing))
            {
                Pages.PostPage.Open(gap.Reply);
                return;
            }

            int index = Rows.IndexOf(gap);
            if (index < 0)
                return; // the list was refreshed while the request was out

            Rows.RemoveAt(index);
            foreach (PostView post in hidden)
                Rows.Insert(index++, FeedMapper.ContextPost(post, lineAbove: true));
        }
        catch (Exception ex)
        {
            LogService.Error("Timeline", "Expanding a thread gap threw", ex);
        }
        finally
        {
            gap.IsLoading = false;
        }
    }

    /// <summary>
    /// Turns feed entries into rows. An entry whose post is already in the list (a repost across a
    /// page boundary, or a post already shown as context for a reply) is skipped, and what an
    /// entry adds is recorded in <paramref name="seen"/>.
    /// </summary>
    private static void AppendPage(FeedPage page, List<object> rows, HashSet<string> seen, bool includeContext)
    {
        if (page.Posts is not null)
            AppendEntries(page.Posts, rows, seen, includeContext);

        // Search hits are bare posts: no repost reason or reply context to build rows from.
        if (page.Hits is not null)
        {
            foreach (PostView hit in page.Hits)
            {
                if (seen.Add(hit.Uri.ToString()))
                    rows.Add(FeedMapper.ToPostItem(hit));
            }
        }
    }

    private static void AppendEntries(IEnumerable<FeedViewPost> entries, List<object> rows, HashSet<string> seen, bool includeContext)
    {
        foreach (FeedViewPost fp in entries)
        {
            if (fp.Post is null || seen.Contains(fp.Post.Uri.ToString()))
                continue;

            foreach (object row in FeedMapper.ToTimelineRows(fp, includeContext))
            {
                if (row is PostItem post)
                    seen.Add(post.AtUri);
                rows.Add(row);
            }
        }
    }

    private void Clear()
    {
        Rows.Clear();
        _cursor = null;
        _loadedAtUtc = DateTimeOffset.MinValue;
        Error = null;
        IsEmpty = false;
    }

    /// <summary>Fetch results, flattened to one shape regardless of which endpoint served the currently
    /// selected feed - the timeline, a feed generator's feed, or a list's feed all page the same way.</summary>
    private readonly record struct FeedPage(bool Succeeded, IEnumerable<FeedViewPost>? Posts, string? Cursor, System.Net.HttpStatusCode StatusCode, string? ErrorMessage, IEnumerable<PostView>? Hits = null)
    {
        /// <summary>True when the fetch produced something to show: feed entries, or search hits for a hashtag tab.</summary>
        public bool HasContent => Posts is not null || Hits is not null;
    }

    private async Task<FeedPage> FetchPageAsync(string? cursor)
    {
        BlueskyAgent agent = BlueskySessionService.Instance.Agent;

        // Labels only come back for the labelers a request names.
        await LabelService.Instance.EnsureLoadedAsync();
        IReadOnlyList<Did>? labelers = LabelService.Instance.Subscribed;

        if (_currentFeed is null || _currentFeed.Kind == FeedTabKind.Timeline)
        {
            AtProtoHttpResult<Timeline> result = await agent.GetTimeline(limit: PageSize, cursor: cursor, subscribedLabelers: labelers);
            return new FeedPage(result.Succeeded, result.Result, result.Result?.Cursor, result.StatusCode, result.AtErrorDetail?.Message);
        }

        if (_currentFeed.Kind == FeedTabKind.Hashtag && _currentFeed.Tag is { } tag)
        {
            AtProtoHttpResult<SearchResults> hits = await agent.SearchPosts("#" + tag, searchOrder: SearchOrder.Latest, limit: PageSize, cursor: cursor, subscribedLabelers: labelers);
            return new FeedPage(hits.Succeeded, null, hits.Result?.Cursor, hits.StatusCode, hits.AtErrorDetail?.Message, hits.Result);
        }

        if (_currentFeed.Uri is not AtUri feedUri)
            return new FeedPage(false, null, null, 0, "That feed has no address.");

        AtProtoHttpResult<PagedViewReadOnlyCollection<FeedViewPost>> pageResult = _currentFeed.Kind == FeedTabKind.List
            ? await agent.GetListFeed(feedUri, limit: PageSize, cursor: cursor, subscribedLabelers: labelers)
            : await agent.GetFeed(feedUri, limit: PageSize, cursor: cursor, subscribedLabelers: labelers);

        return new FeedPage(pageResult.Succeeded, pageResult.Result, pageResult.Result?.Cursor, pageResult.StatusCode, pageResult.AtErrorDetail?.Message);
    }

    private static string Describe(System.Net.HttpStatusCode status, string? message) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Your session expired. Sign in again.",
        System.Net.HttpStatusCode.TooManyRequests => "Bluesky is rate limiting requests. Try again shortly.",
        0 => "Couldn't reach Bluesky. Check your connection.",
        _ => string.IsNullOrEmpty(message) ? "Couldn't load your timeline." : message
    };
}

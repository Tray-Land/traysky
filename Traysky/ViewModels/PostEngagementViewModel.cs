using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Feed;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// Who liked and who reposted one post, and the posts quoting it. All three lists are fetched
/// when the page opens and page in independently as each is scrolled.
/// </summary>
public sealed partial class PostEngagementViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SemaphoreSlim _likesGate = new(1, 1);
    private readonly SemaphoreSlim _repostsGate = new(1, 1);
    private readonly SemaphoreSlim _quotesGate = new(1, 1);
    private AtUri? _uri;
    private string? _likesCursor;
    private string? _repostsCursor;
    private string? _quotesCursor;

    public ObservableCollection<ActorItem> Likes { get; } = [];
    public ObservableCollection<ActorItem> Reposts { get; } = [];
    public ObservableCollection<PostItem> Quotes { get; } = [];

    [ObservableProperty]
    public partial PostItem? Post { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMoreLikes => !string.IsNullOrEmpty(_likesCursor);
    public bool HasMoreReposts => !string.IsNullOrEmpty(_repostsCursor);

    public bool HasMoreQuotes => !string.IsNullOrEmpty(_quotesCursor);

    public string QuotesHeader => Post is { QuoteCount: > 0 } p ? $"Quotes ({p.QuoteCount})" : "Quotes";

    public string LikesHeader => Post is { LikeCount: > 0 } p ? $"Likes ({p.LikeCount})" : "Likes";
    public string RepostsHeader => Post is { RepostCount: > 0 } p ? $"Reposts ({p.RepostCount})" : "Reposts";

    public async Task LoadAsync(PostItem post)
    {
        Post = post;
        OnPropertyChanged(nameof(LikesHeader));
        OnPropertyChanged(nameof(RepostsHeader));
        OnPropertyChanged(nameof(QuotesHeader));

        Error = null;
        Likes.Clear();
        Reposts.Clear();
        Quotes.Clear();
        _likesCursor = null;
        _repostsCursor = null;
        _quotesCursor = null;

        if (PreviewMode.IsEnabled)
        {
            foreach (PostItem sample in PreviewMode.SamplePosts().Take(2))
                Quotes.Add(sample);
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn || string.IsNullOrEmpty(post.AtUri))
            return;

        _uri = new AtUri(post.AtUri);

        IsLoading = true;
        try
        {
            await Task.WhenAll(LoadLikesAsync(reset: true), LoadRepostsAsync(reset: true), LoadQuotesAsync(reset: true));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task LoadMoreLikesAsync() => HasMoreLikes ? LoadLikesAsync(reset: false) : Task.CompletedTask;

    public Task LoadMoreRepostsAsync() => HasMoreReposts ? LoadRepostsAsync(reset: false) : Task.CompletedTask;

    public Task LoadMoreQuotesAsync() => HasMoreQuotes ? LoadQuotesAsync(reset: false) : Task.CompletedTask;

    private async Task LoadQuotesAsync(bool reset)
    {
        if (_uri is null || !await _quotesGate.WaitAsync(0))
            return;

        try
        {
            AtProtoHttpResult<QuotesCollection> result = await BlueskySessionService.Instance.Agent.GetQuotes(
                _uri,
                limit: PageSize,
                cursor: reset ? null : _quotesCursor);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Engagement", $"GetQuotes failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = LoadError(result.StatusCode);
                return;
            }

            if (reset)
                Quotes.Clear();

            foreach (PostView quote in result.Result)
                Quotes.Add(FeedMapper.ToPostItem(quote));

            _quotesCursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("Engagement", $"GetQuotes threw: {ex.Message}");
            Error = "Couldn't load quotes of this post.";
        }
        finally
        {
            _quotesGate.Release();
        }
    }

    private async Task LoadLikesAsync(bool reset)
    {
        if (_uri is null || !await _likesGate.WaitAsync(0))
            return;

        try
        {
            AtProtoHttpResult<LikesCollection> result = await BlueskySessionService.Instance.Agent.GetLikes(
                _uri,
                limit: PageSize,
                cursor: reset ? null : _likesCursor);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Engagement", $"GetLikes failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = LoadError(result.StatusCode);
                return;
            }

            if (reset)
                Likes.Clear();

            foreach (idunno.Bluesky.Feed.Likes.Like like in result.Result)
            {
                if (like.Actor is not null)
                    Likes.Add(FeedMapper.ToActorItem(like.Actor));
            }

            _likesCursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("Engagement", $"GetLikes threw: {ex.Message}");
            Error = "Couldn't load who liked this post.";
        }
        finally
        {
            _likesGate.Release();
        }
    }

    private async Task LoadRepostsAsync(bool reset)
    {
        if (_uri is null || !await _repostsGate.WaitAsync(0))
            return;

        try
        {
            AtProtoHttpResult<RepostedBy> result = await BlueskySessionService.Instance.Agent.GetRepostedBy(
                _uri,
                limit: PageSize,
                cursor: reset ? null : _repostsCursor);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Engagement", $"GetRepostedBy failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = LoadError(result.StatusCode);
                return;
            }

            if (reset)
                Reposts.Clear();

            foreach (ProfileView profile in result.Result)
                Reposts.Add(FeedMapper.ToActorItem(profile));

            _repostsCursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("Engagement", $"GetRepostedBy threw: {ex.Message}");
            Error = "Couldn't load who reposted this post.";
        }
        finally
        {
            _repostsGate.Release();
        }
    }

    private static string LoadError(System.Net.HttpStatusCode status) => status == 0
        ? "Couldn't reach Bluesky. Check your connection."
        : "Couldn't load likes, reposts and quotes.";

    [RelayCommand]
    private Task Retry() => Post is null ? Task.CompletedTask : LoadAsync(Post);
}

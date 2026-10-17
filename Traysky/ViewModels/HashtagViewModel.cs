using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky.Feed;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The newest posts carrying one hashtag, found with the post search. Opened for a tag
/// (with or without the leading #).
/// </summary>
public sealed partial class HashtagViewModel : ObservableObject
{
    private const int PageSize = 20;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cursor;

    public ObservableCollection<PostItem> Posts { get; } = [];

    [ObservableProperty]
    public partial string Tag { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasLoaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowLabel))]
    public partial bool IsFollowing { get; private set; }

    public string FollowLabel => IsFollowing ? "Following" : "Follow";

    public string TagDisplay => "#" + Tag;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsEmpty => HasLoaded && Posts.Count == 0 && !HasError;

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public string WebUrl => BlueskyLinks.HashtagUrl(Tag);

    public async Task LoadAsync(string tag)
    {
        Tag = tag.Trim().TrimStart('#');
        OnPropertyChanged(nameof(TagDisplay));
        IsFollowing = FeedsService.Instance.IsFollowingTag(Tag);
        Posts.Clear();
        _cursor = null;
        Error = null;
        HasLoaded = false;

        if (Tag.Length == 0 || !BlueskySessionService.Instance.IsSignedIn || PreviewMode.IsEnabled)
        {
            HasLoaded = true;
            return;
        }

        await LoadPageAsync(reset: true);
    }

    public Task LoadMoreAsync() => HasMore ? LoadPageAsync(reset: false) : Task.CompletedTask;

    private async Task LoadPageAsync(bool reset)
    {
        if (!await _gate.WaitAsync(0))
            return;

        IsLoading = true;
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            AtProtoHttpResult<SearchResults> result = await BlueskySessionService.Instance.Agent.SearchPosts(
                "#" + Tag,
                searchOrder: SearchOrder.Latest,
                limit: PageSize,
                cursor: reset ? null : _cursor,
                subscribedLabelers: LabelService.Instance.Subscribed);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Hashtag", $"SearchPosts failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                if (reset)
                {
                    Error = result.StatusCode == 0
                        ? "Couldn't reach Bluesky. Check your connection."
                        : "Couldn't load posts for this hashtag.";
                }
                return;
            }

            foreach (var post in result.Result)
                Posts.Add(FeedMapper.ToPostItem(post));

            _cursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("Hashtag", $"SearchPosts threw: {ex.Message}");
            if (reset)
                Error = "Couldn't load posts for this hashtag.";
        }
        finally
        {
            IsLoading = false;
            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
            _gate.Release();
        }
    }

    /// <summary>Adds or removes this hashtag as a tab in the feed row.</summary>
    [RelayCommand]
    private void ToggleFollow()
    {
        if (Tag.Length == 0)
            return;

        if (IsFollowing)
            FeedsService.Instance.UnfollowTag(Tag);
        else
            FeedsService.Instance.FollowTag(Tag);
        IsFollowing = FeedsService.Instance.IsFollowingTag(Tag);
    }

    [RelayCommand]
    private Task Retry() => LoadAsync(Tag);

    [RelayCommand]
    private Task OpenInBrowser() => RichTextBuilder.OpenAsync(WebUrl);
}

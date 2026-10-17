using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// The signed-in user's saved posts, newest bookmark first. Un-bookmarking a post from here
/// drops it from the list.
/// </summary>
public sealed partial class BookmarksViewModel : ObservableObject
{
    private const int PageSize = 25;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cursor;

    public ObservableCollection<PostItem> Posts { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasLoaded { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsEmpty => HasLoaded && Posts.Count == 0 && !HasError;

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public async Task LoadAsync()
    {
        Posts.Clear();
        _cursor = null;
        Error = null;
        HasLoaded = false;

        if (!BlueskySessionService.Instance.IsSignedIn || PreviewMode.IsEnabled)
        {
            HasLoaded = true;
            return;
        }

        await LoadPageAsync(reset: true);
    }

    public Task LoadMoreAsync() => HasMore ? LoadPageAsync(reset: false) : Task.CompletedTask;

    /// <summary>Removes a post that was un-bookmarked while this page is showing it.</summary>
    public void OnBookmarkChanged(PostItem changed)
    {
        if (changed.IsBookmarked)
            return;

        PostItem? row = Posts.FirstOrDefault(p => p.AtUri == changed.AtUri);
        if (row is not null)
            Posts.Remove(row);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadPageAsync(bool reset)
    {
        if (!await _gate.WaitAsync(0))
            return;

        IsLoading = true;
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            var result = await BlueskySessionService.Instance.Agent.GetBookmarks(
                limit: PageSize,
                cursor: reset ? null : _cursor,
                subscribedLabelers: LabelService.Instance.Subscribed);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Bookmarks", $"GetBookmarks failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                if (reset)
                {
                    Error = result.StatusCode == 0
                        ? "Couldn't reach Bluesky. Check your connection."
                        : "Couldn't load your bookmarks.";
                }
                return;
            }

            // Items can also be blocked / not-found / deleted posts; only real posts have a card.
            foreach (var bookmark in result.Result)
            {
                if (bookmark.Item is PostView post)
                    Posts.Add(FeedMapper.ToPostItem(post));
            }

            _cursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("Bookmarks", $"GetBookmarks threw: {ex.Message}");
            if (reset)
                Error = "Couldn't load your bookmarks.";
        }
        finally
        {
            IsLoading = false;
            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
            _gate.Release();
        }
    }

    [RelayCommand]
    private Task Retry() => LoadAsync();
}

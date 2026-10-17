using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// One post with its thread: ancestors above, replies below. The seed post renders at once
/// from what the caller already had; the thread fetch then replaces it with the hydrated
/// version and fills in the rest.
/// </summary>
public sealed partial class PostPageViewModel : ObservableObject
{
    public ObservableCollection<ThreadRow> Rows { get; } = [];

    [ObservableProperty]
    public partial PostItem? Post { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    public partial string? Context { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool HasNoReplies { get; private set; }

    public bool HasContext => !string.IsNullOrEmpty(Context);

    public bool HasError => !string.IsNullOrEmpty(Error);

    public async Task LoadAsync(PostPageArgs args)
    {
        _lastArgs = args;
        Post = args.Post;
        Context = args.Context;
        Error = null;
        HasNoReplies = false;
        HasHiddenReplies = false;
        HiddenRepliesError = null;
        CanSort = false;

        Rows.Clear();
        if (args.Post is not null)
            Rows.Add(new ThreadRow { Post = args.Post, Depth = 0, IsMain = true });

        string? atUri = args.EffectiveAtUri;
        if (atUri is null)
        {
            Error = "This post is no longer available.";
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn || PreviewMode.IsEnabled)
        {
            HasNoReplies = args.Post?.ReplyCount == 0;
            return;
        }

        IsLoading = true;
        try
        {
            ThreadLoadResult result = await ThreadService.LoadAsync(atUri, _sort);

            if (!result.Succeeded && result.Status != System.Net.HttpStatusCode.OK)
            {
                Error = result.Status switch
                {
                    System.Net.HttpStatusCode.NotFound => "This post has been deleted.",
                    0 => "Couldn't reach Bluesky. Check your connection.",
                    _ => "Couldn't load the thread."
                };
                return;
            }

            List<ThreadRow> rows = result.Rows;
            if (rows.Count == 0)
            {
                Error = "This post is no longer available.";
                return;
            }

            _atUri = atUri;
            CanSort = result.UsedV2;
            HasHiddenReplies = result.HasHiddenReplies;
            HiddenRepliesError = null;

            // Carry the seed's optimistic like/repost state forward if the server view is
            // older than a click the user just made on the card that opened this page.
            foreach (ThreadRow row in rows)
            {
                if (row.IsMain)
                {
                    Post = row.Post;
                    break;
                }
            }

            Rows.Clear();
            foreach (ThreadRow row in rows)
                Rows.Add(row);

            HasNoReplies = (rows.Count == 1 || !rows.Exists(r => r.Depth > 0)) && !result.HasHiddenReplies;
            OnPropertyChanged(nameof(ShowSort));
        }
        catch (Exception ex)
        {
            LogService.Error("Thread", "Thread load threw", ex);
            Error = "Couldn't load the thread.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Labels for the sort picker, in <see cref="ThreadSort"/> order.</summary>
    public IReadOnlyList<string> SortLabels { get; } =
        [ThreadSortOptions.Label(ThreadSort.Top), ThreadSortOptions.Label(ThreadSort.Oldest), ThreadSortOptions.Label(ThreadSort.Newest)];

    public int SortIndex
    {
        get => (int)_sort;
        set
        {
            if (value < 0 || value >= SortLabels.Count || value == (int)_sort)
                return;

            _sort = (ThreadSort)value;
            SettingsService.ThreadSort = _sort;
            OnPropertyChanged();

            if (_lastArgs is not null)
                _ = LoadAsync(_lastArgs);
        }
    }

    /// <summary>The sort picker only applies to v2 threads that have replies to sort.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSort))]
    public partial bool CanSort { get; private set; }

    public bool ShowSort => CanSort && Rows.Count > 1;

    [ObservableProperty]
    public partial bool HasHiddenReplies { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenRepliesError))]
    public partial string? HiddenRepliesError { get; private set; }

    public bool HasHiddenRepliesError => !string.IsNullOrEmpty(HiddenRepliesError);

    [ObservableProperty]
    public partial bool IsLoadingHidden { get; private set; }

    /// <summary>Fetches replies the author hid or the viewer muted and appends them to the thread.</summary>
    [RelayCommand]
    private async Task ShowHiddenReplies()
    {
        if (_atUri is null || IsLoadingHidden)
            return;

        IsLoadingHidden = true;
        HiddenRepliesError = null;
        try
        {
            List<ThreadRow>? hidden = await ThreadService.LoadOtherRepliesAsync(_atUri, Post?.AuthorHandle);
            if (hidden is null)
            {
                HiddenRepliesError = "Couldn't load hidden replies.";
                return;
            }

            // Drop any the thread already shows (the two responses can overlap).
            HashSet<string?> shown = [.. Rows.Select(r => r.Post.AtUri)];
            foreach (ThreadRow row in hidden)
            {
                if (shown.Add(row.Post.AtUri))
                    Rows.Add(row);
            }

            HasHiddenReplies = false;
            HasNoReplies = false;
            OnPropertyChanged(nameof(ShowSort));
        }
        finally
        {
            IsLoadingHidden = false;
        }
    }

    private ThreadSort _sort = SettingsService.ThreadSort;
    private string? _atUri;

    [RelayCommand]
    private Task Retry() => _lastArgs is null ? Task.CompletedTask : LoadAsync(_lastArgs);

    private PostPageArgs? _lastArgs;

    [RelayCommand]
    private Task OpenInBrowser() => RichTextBuilder.OpenAsync(Post?.WebUrl);
}

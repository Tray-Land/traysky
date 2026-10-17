using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.Bluesky.Feed;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// Posts, people and starter packs matching a query, one section each. A section loads the first
/// time its tab is shown for the current query and pages in as it is scrolled; a new query resets
/// all three. Also owns the search box's suggestions: recent queries plus a debounced actor typeahead.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int PageSize = 25;
    private const int TypeaheadDelayMs = 250;

    private readonly Section<PostItem> _posts = new();
    private readonly Section<ActorItem> _people = new();
    private readonly Section<StarterPackItem> _packs = new();
    private CancellationTokenSource? _typeahead;
    private int _generation;

    public ObservableCollection<PostItem> Posts => _posts.Items;
    public ObservableCollection<ActorItem> People => _people.Items;
    public ObservableCollection<StarterPackItem> StarterPacks => _packs.Items;

    /// <summary>What the search box's dropdown shows: matching recent queries, then actors from the typeahead.</summary>
    public ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    /// <summary>The query the results belong to; empty before the first search.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    public partial string Query { get; private set; } = string.Empty;

    /// <summary>True while the Posts tab ranks by latest instead of top.</summary>
    [ObservableProperty]
    public partial bool LatestPosts { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasQuery => Query.Length > 0;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMorePosts => _posts.HasMore;
    public bool HasMorePeople => _people.HasMore;
    public bool HasMorePacks => _packs.HasMore;

    /// <summary>Whether each section has finished a load for the current query, so an empty list can say "no results".</summary>
    public bool PostsLoaded => _posts.Loaded;
    public bool PeopleLoaded => _people.Loaded;
    public bool PacksLoaded => _packs.Loaded;

    private static bool CanSearch => BlueskySessionService.Instance.IsSignedIn && !PreviewMode.IsEnabled;

    /// <summary>Starts a new search. The caller has already routed a lone #tag to the hashtag page.</summary>
    public void Submit(string? query)
    {
        string normalized = SearchQueryPolicy.Normalize(query);
        if (normalized.Length == 0)
            return;

        CancelTypeahead();
        SettingsService.RecentSearches = SearchQueryPolicy.WithRecentQuery(SettingsService.RecentSearches, normalized);

        _generation++;
        Query = normalized;
        Error = null;
        _posts.Reset();
        _people.Reset();
        _packs.Reset();
        RaiseSectionState();
        SectionsReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the results were cleared (new query, retry or posts ranking change) so the page can load the visible tab.</summary>
    public event EventHandler? SectionsReset;

    /// <summary>Loads the posts / people / packs section if it hasn't been for this query.</summary>
    public Task EnsurePostsAsync() => _posts.Loaded || !HasQuery ? Task.CompletedTask : LoadPostsAsync(reset: true);
    public Task EnsurePeopleAsync() => _people.Loaded || !HasQuery ? Task.CompletedTask : LoadPeopleAsync(reset: true);
    public Task EnsurePacksAsync() => _packs.Loaded || !HasQuery ? Task.CompletedTask : LoadPacksAsync(reset: true);

    public Task LoadMorePostsAsync() => _posts.HasMore ? LoadPostsAsync(reset: false) : Task.CompletedTask;
    public Task LoadMorePeopleAsync() => _people.HasMore ? LoadPeopleAsync(reset: false) : Task.CompletedTask;
    public Task LoadMorePacksAsync() => _packs.HasMore ? LoadPacksAsync(reset: false) : Task.CompletedTask;

    partial void OnLatestPostsChanged(bool value)
    {
        // Switching the ranking re-queries the posts; the page reloads the visible tab.
        if (!HasQuery)
            return;
        _posts.Reset();
        OnPropertyChanged(nameof(PostsLoaded));
        OnPropertyChanged(nameof(HasMorePosts));
        SectionsReset?.Invoke(this, EventArgs.Empty);
    }

    private Task LoadPostsAsync(bool reset) => LoadPageAsync(_posts, reset, "posts", () => PreviewMode.SamplePosts(), async cursor =>
    {
        await LabelService.Instance.EnsureLoadedAsync();
        var result = await BlueskySessionService.Instance.Agent.SearchPosts(
            Query,
            searchOrder: LatestPosts ? SearchOrder.Latest : SearchOrder.Top,
            limit: PageSize,
            cursor: cursor,
            subscribedLabelers: LabelService.Instance.Subscribed);

        return result.Succeeded && result.Result is not null
            ? new PageResult<PostItem>(true, result.StatusCode, result.Result.Select(post => FeedMapper.ToPostItem(post)).ToList(), result.Result.Cursor)
            : PageResult<PostItem>.Failed(result.StatusCode, result.AtErrorDetail?.Error);
    });

    private Task LoadPeopleAsync(bool reset) => LoadPageAsync(_people, reset, "people", () => PreviewMode.SampleSearchActors(), async cursor =>
    {
        await LabelService.Instance.EnsureLoadedAsync();
        var result = await BlueskySessionService.Instance.Agent.SearchActors(
            Query,
            limit: PageSize,
            cursor: cursor,
            subscribedLabelers: LabelService.Instance.Subscribed);

        return result.Succeeded && result.Result is not null
            ? new PageResult<ActorItem>(true, result.StatusCode, result.Result.Select(profile => FeedMapper.ToActorItem(profile)).ToList(), result.Result.Cursor)
            : PageResult<ActorItem>.Failed(result.StatusCode, result.AtErrorDetail?.Error);
    });

    private Task LoadPacksAsync(bool reset) => LoadPageAsync(_packs, reset, "starter packs", () => PreviewMode.SampleStarterPacks(), async cursor =>
    {
        var result = await BlueskySessionService.Instance.Agent.SearchStarterPacks(Query, limit: PageSize, cursor: cursor);

        return result.Succeeded && result.Result is not null
            ? new PageResult<StarterPackItem>(true, result.StatusCode, result.Result.Select(pack => FeedMapper.ToStarterPackItem(pack)).ToList(), result.Result.Cursor)
            : PageResult<StarterPackItem>.Failed(result.StatusCode, result.AtErrorDetail?.Error);
    });

    private async Task LoadPageAsync<T>(
        Section<T> section,
        bool reset,
        string what,
        Func<List<T>> preview,
        Func<string?, Task<PageResult<T>>> fetch)
    {
        if (section.Loading)
            return;

        int generation = _generation;
        int epoch = section.Epoch;
        section.Loading = true;
        IsLoading = true;
        try
        {
            if (PreviewMode.IsEnabled)
            {
                if (reset)
                {
                    foreach (T item in preview())
                        section.Items.Add(item);
                }
                section.Cursor = null;
                section.Loaded = true;
                return;
            }

            if (!CanSearch)
                return;

            PageResult<T> page = await fetch(reset ? null : section.Cursor);

            // A newer query or ranking replaced this one while the request was out.
            if (generation != _generation || epoch != section.Epoch)
                return;

            if (!page.Succeeded)
            {
                LogService.Warn("Search", $"Searching {what} failed: {(int)page.Status} {page.ErrorName}");
                if (reset)
                {
                    Error = page.Status == 0
                        ? "Couldn't reach Bluesky. Check your connection."
                        : $"Couldn't search {what}.";
                }
                return;
            }

            Error = null;
            foreach (T item in page.Items)
                section.Items.Add(item);
            section.Cursor = page.Cursor;
            section.Loaded = true;
        }
        catch (Exception ex)
        {
            LogService.Warn("Search", $"Searching {what} threw: {ex.Message}");
            if (reset && generation == _generation && epoch == section.Epoch)
                Error = $"Couldn't search {what}.";
        }
        finally
        {
            // Reset already cleared Loading for a replaced result set; don't clobber its successor's flag.
            if (epoch == section.Epoch)
                section.Loading = false;
            IsLoading = _posts.Loading || _people.Loading || _packs.Loading;
            RaiseSectionState();
        }
    }

    private void RaiseSectionState()
    {
        OnPropertyChanged(nameof(PostsLoaded));
        OnPropertyChanged(nameof(PeopleLoaded));
        OnPropertyChanged(nameof(PacksLoaded));
        OnPropertyChanged(nameof(HasMorePosts));
        OnPropertyChanged(nameof(HasMorePeople));
        OnPropertyChanged(nameof(HasMorePacks));
    }

    /// <summary>Refreshes the dropdown for what's typed: matching recents at once, actors after a short pause.</summary>
    public async Task UpdateSuggestionsAsync(string? text)
    {
        CancelTypeahead();

        string normalized = SearchQueryPolicy.Normalize(text);
        Suggestions.Clear();
        foreach (string recent in SearchQueryPolicy.FilterRecents(SettingsService.RecentSearches, normalized).Take(5))
            Suggestions.Add(SearchSuggestion.ForRecent(recent));

        if (!SearchQueryPolicy.ShouldSuggest(normalized) || !CanSearch)
            return;

        var cts = _typeahead = new CancellationTokenSource();
        try
        {
            await Task.Delay(TypeaheadDelayMs, cts.Token);
            var result = await BlueskySessionService.Instance.Agent.SearchActorsTypeahead(
                normalized,
                limit: 5,
                subscribedLabelers: LabelService.Instance.Subscribed,
                cancellationToken: cts.Token);

            if (cts.IsCancellationRequested || !result.Succeeded || result.Result is null)
                return;

            foreach (var profile in result.Result)
                Suggestions.Add(SearchSuggestion.ForActor(FeedMapper.ToActorItem(profile)));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogService.Warn("Search", $"Typeahead threw: {ex.Message}");
        }
    }

    public void ClearSuggestions()
    {
        CancelTypeahead();
        Suggestions.Clear();
    }

    private void CancelTypeahead()
    {
        _typeahead?.Cancel();
        _typeahead = null;
    }

    /// <summary>Forgets one remembered query (the dropdown's remove affordance).</summary>
    public void ForgetRecent(string query) =>
        SettingsService.RecentSearches = SearchQueryPolicy.WithoutRecentQuery(SettingsService.RecentSearches, query);

    [RelayCommand]
    private Task Retry()
    {
        if (!HasQuery)
            return Task.CompletedTask;

        string query = Query;
        Submit(query);
        return Task.CompletedTask;
    }

    private sealed class Section<T>
    {
        public ObservableCollection<T> Items { get; } = [];
        public string? Cursor { get; set; }
        public bool Loaded { get; set; }
        public bool Loading { get; set; }

        /// <summary>Bumped by <see cref="Reset"/> so a response for a replaced result set is dropped.</summary>
        public int Epoch { get; private set; }

        public bool HasMore => !string.IsNullOrEmpty(Cursor);

        public void Reset()
        {
            Items.Clear();
            Cursor = null;
            Loaded = false;
            Loading = false;
            Epoch++;
        }
    }

    private sealed record PageResult<T>(bool Succeeded, HttpStatusCode Status, IReadOnlyList<T> Items, string? Cursor, string? ErrorName = null)
    {
        public static PageResult<T> Failed(HttpStatusCode status, string? errorName) => new(false, status, [], null, errorName);
    }
}

/// <summary>One row in the search box's dropdown: a remembered query or an account.</summary>
public sealed class SearchSuggestion
{
    public string Text { get; init; } = string.Empty;
    public ActorItem? Actor { get; init; }
    public bool IsRecent => Actor is null;

    public string Glyph => IsRecent ? "" : "";

    public static SearchSuggestion ForRecent(string query) => new() { Text = query };

    public static SearchSuggestion ForActor(ActorItem actor) => new() { Text = actor.NameOrHandle, Actor = actor };

    public override string ToString() => Text;
}

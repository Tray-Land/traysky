using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// Search: posts (top or latest), people and starter packs as three tabs. Navigation parameter is
/// an optional starting query. A lone <c>#tag</c> goes to the hashtag page instead.
/// </summary>
public sealed partial class SearchPage : Page
{
    private readonly Dictionary<ListView, ScrollViewer> _scrollViewers = [];

    public SearchViewModel ViewModel { get; } = new();

    public SearchPage()
    {
        InitializeComponent();
        SearchBox.ItemsSource = ViewModel.Suggestions;
        Loaded += (_, _) => EnsureScrollHook(SelectedList);

        ViewModel.SectionsReset += (_, _) => LoadSelectedTab();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Posts.CollectionChanged += OnListChanged;
        ViewModel.People.CollectionChanged += OnListChanged;
        ViewModel.StarterPacks.CollectionChanged += OnListChanged;

        ShowSelectedTab();
        UpdateStatusText();
    }

    /// <summary>Navigates to the search page, optionally running <paramref name="query"/> straight away.</summary>
    public static void Open(string? query = null)
    {
        string? tag = SearchQueryPolicy.AsHashtag(query);
        if (tag is not null)
        {
            RememberQuery(query);
            HashtagPage.Open(tag);
            return;
        }

        NavigationService.Instance.Navigate(typeof(SearchPage), SearchQueryPolicy.Normalize(query));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string query && query.Length > 0)
        {
            SearchBox.Text = query;
            ViewModel.Submit(query);
        }
        else
        {
            SearchBox.Focus(FocusState.Programmatic);
        }
    }

    private static void RememberQuery(string? query) =>
        SettingsService.RecentSearches = SearchQueryPolicy.WithRecentQuery(SettingsService.RecentSearches, query);

    private ListView SelectedList =>
        Tabs.SelectedItem == PeopleTab ? PeopleList :
        Tabs.SelectedItem == PacksTab ? PackList :
        PostList;

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // IsSelected in XAML can raise SelectionChanged mid-InitializeComponent, before the lists exist.
        if (PackList is null || StatusText is null)
            return;

        ShowSelectedTab();
        LoadSelectedTab();
    }

    private void ShowSelectedTab()
    {
        if (PackList is null || StatusText is null)
            return;

        PostList.Visibility = Tabs.SelectedItem == PostsTab ? Visibility.Visible : Visibility.Collapsed;
        PeopleList.Visibility = Tabs.SelectedItem == PeopleTab ? Visibility.Visible : Visibility.Collapsed;
        PackList.Visibility = Tabs.SelectedItem == PacksTab ? Visibility.Visible : Visibility.Collapsed;

        EnsureScrollHook(SelectedList);
        UpdateStatusText();
    }

    private void LoadSelectedTab()
    {
        EnsureScrollHook(SelectedList);

        if (Tabs.SelectedItem == PeopleTab)
            _ = ViewModel.EnsurePeopleAsync();
        else if (Tabs.SelectedItem == PacksTab)
            _ = ViewModel.EnsurePacksAsync();
        else
            _ = ViewModel.EnsurePostsAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SearchViewModel.IsLoading) or nameof(SearchViewModel.HasError) or nameof(SearchViewModel.Query)
            or nameof(SearchViewModel.PostsLoaded) or nameof(SearchViewModel.PeopleLoaded) or nameof(SearchViewModel.PacksLoaded))
        {
            UpdateStatusText();
        }
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateStatusText();

    /// <summary>The line shown over an empty list: a prompt before any search, "no results" after an empty one.</summary>
    private void UpdateStatusText()
    {
        if (StatusText is null)
            return;

        string? text = null;
        if (!ViewModel.HasQuery)
        {
            text = "Search posts, people and starter packs.";
        }
        else if (!ViewModel.IsLoading && !ViewModel.HasError)
        {
            if (Tabs.SelectedItem == PeopleTab && ViewModel.PeopleLoaded && ViewModel.People.Count == 0)
                text = $"No people found for “{ViewModel.Query}”.";
            else if (Tabs.SelectedItem == PacksTab && ViewModel.PacksLoaded && ViewModel.StarterPacks.Count == 0)
                text = $"No starter packs found for “{ViewModel.Query}”.";
            else if (Tabs.SelectedItem == PostsTab && ViewModel.PostsLoaded && ViewModel.Posts.Count == 0)
                text = $"No posts found for “{ViewModel.Query}”.";
        }

        StatusText.Text = text ?? string.Empty;
        StatusText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _ = ViewModel.UpdateSuggestionsAsync(sender.Text);
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        // An account suggestion opens the profile rather than becoming the query.
        if (args.SelectedItem is SearchSuggestion { Actor: { } actor })
        {
            sender.Text = string.Empty;
            ViewModel.ClearSuggestions();
            ProfilePage.Open(actor.ProfileKey);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // The account case was already handled by SuggestionChosen.
        if (args.ChosenSuggestion is SearchSuggestion { Actor: not null })
            return;

        string query = args.ChosenSuggestion is SearchSuggestion recent ? recent.Text : args.QueryText;
        string? tag = SearchQueryPolicy.AsHashtag(query);
        if (tag is not null)
        {
            RememberQuery(query);
            HashtagPage.Open(tag);
            return;
        }

        sender.Text = SearchQueryPolicy.Normalize(query);
        ViewModel.Submit(query);
    }

    private void PostCard_ThreadRequested(object? sender, PostItem post) => PostPage.Open(post);

    private void PeopleList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    private void PackList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StarterPackItem pack)
            StarterPackPage.Open(pack);
    }

    /// <summary>Hooks paging onto a list's ScrollViewer once its template exists (a collapsed list has none yet).</summary>
    private void EnsureScrollHook(ListView list)
    {
        if (_scrollViewers.ContainsKey(list))
            return;

        list.UpdateLayout();
        ScrollViewer? scrollViewer = FindScrollViewer(list);
        if (scrollViewer is null)
            return;

        _scrollViewers[list] = scrollViewer;
        scrollViewer.ViewChanged += (_, e) =>
        {
            if (e.IsIntermediate)
                return;

            double remaining = scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset;
            if (remaining >= scrollViewer.ViewportHeight * 2)
                return;

            if (list == PostList && ViewModel.HasMorePosts)
                _ = ViewModel.LoadMorePostsAsync();
            else if (list == PeopleList && ViewModel.HasMorePeople)
                _ = ViewModel.LoadMorePeopleAsync();
            else if (list == PackList && ViewModel.HasMorePacks)
                _ = ViewModel.LoadMorePacksAsync();
        };
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            ScrollViewer? found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }
        return null;
    }
}

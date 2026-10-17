using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// Who liked and who reposted a post, one tab each. Opened from the info button on the main
/// post of a thread page; tapping a person opens their profile.
/// </summary>
public sealed partial class PostEngagementPage : Page
{
    private ScrollViewer? _scrollViewer;
    private ScrollViewer? _quotesScrollViewer;

    public PostEngagementViewModel ViewModel { get; } = new();

    public PostEngagementPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        QuotesList.Loaded += OnLoaded; // collapsed until its tab is picked, so it loads later

        ViewModel.Likes.CollectionChanged += OnListChanged;
        ViewModel.Reposts.CollectionChanged += OnListChanged;
        ViewModel.Quotes.CollectionChanged += OnListChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ShowSelectedList();
    }

    /// <summary>Navigates to a post's likes/reposts page.</summary>
    public static void Open(PostItem post) => NavigationService.Instance.Navigate(typeof(PostEngagementPage), post);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is PostItem post)
        {
            if (post.LikeCount == 0 && post.RepostCount > 0)
                RepostsTab.IsSelected = true;
            else if (post.LikeCount == 0 && post.RepostCount == 0 && post.QuoteCount > 0)
                QuotesTab.IsSelected = true;
            _ = ViewModel.LoadAsync(post);
        }
    }

    private bool ShowingReposts => Tabs.SelectedItem == RepostsTab;

    private bool ShowingQuotes => Tabs.SelectedItem == QuotesTab;

    private ObservableCollection<ActorItem> SelectedList => ShowingReposts ? ViewModel.Reposts : ViewModel.Likes;

    private void PostCard_ThreadRequested(object? sender, PostItem post) => PostPage.Open(post);

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowSelectedList();

    private void ShowSelectedList()
    {
        // IsSelected in XAML can raise SelectionChanged mid-InitializeComponent, before the list exists.
        if (PeopleList is null || EmptyText is null || QuotesList is null)
            return;

        PeopleList.Visibility = ShowingQuotes ? Visibility.Collapsed : Visibility.Visible;
        QuotesList.Visibility = ShowingQuotes ? Visibility.Visible : Visibility.Collapsed;

        if (!ShowingQuotes)
            PeopleList.ItemsSource = SelectedList;
        (ShowingQuotes ? _quotesScrollViewer : _scrollViewer)?.ChangeView(null, 0, null, disableAnimation: true);
        UpdateEmptyText();
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyText();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PostEngagementViewModel.IsLoading) or nameof(PostEngagementViewModel.HasError))
            UpdateEmptyText();
    }

    private void UpdateEmptyText()
    {
        EmptyText.Text = ShowingReposts ? "No reposts yet" : "No likes yet";
        bool settled = !ViewModel.IsLoading && !ViewModel.HasError;
        EmptyText.Visibility = settled && !ShowingQuotes && SelectedList.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        QuotesEmptyText.Visibility = settled && ShowingQuotes && ViewModel.Quotes.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PeopleList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is null)
        {
            _scrollViewer = FindScrollViewer(PeopleList);
            if (_scrollViewer is not null)
                _scrollViewer.ViewChanged += OnViewChanged;
        }

        // The quotes list is collapsed until its tab is picked, so its template may not be applied yet.
        if (_quotesScrollViewer is null)
        {
            _quotesScrollViewer = FindScrollViewer(QuotesList);
            if (_quotesScrollViewer is not null)
                _quotesScrollViewer.ViewChanged += OnViewChanged;
        }
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.IsIntermediate)
            return;

        double remaining = viewer.ScrollableHeight - viewer.VerticalOffset;
        if (remaining >= viewer.ViewportHeight * 2)
            return;

        if (ShowingQuotes)
            _ = ViewModel.LoadMoreQuotesAsync();
        else
            _ = ShowingReposts ? ViewModel.LoadMoreRepostsAsync() : ViewModel.LoadMoreLikesAsync();
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

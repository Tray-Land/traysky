using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// An account's followers and following, one tab each. Opened from the counts on a profile;
/// tapping a person opens their profile.
/// </summary>
public sealed partial class FollowListPage : Page
{
    private ScrollViewer? _scrollViewer;

    public FollowListViewModel ViewModel { get; } = new();

    public FollowListPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        ViewModel.Followers.CollectionChanged += OnListChanged;
        ViewModel.Following.CollectionChanged += OnListChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ShowSelectedList();
    }

    /// <summary>Navigates to an account's followers (or following) list.</summary>
    public static void Open(string actor, bool showFollowing, int followersCount, int followsCount)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return;
        NavigationService.Instance.Navigate(typeof(FollowListPage), new FollowListArgs(actor, showFollowing, followersCount, followsCount));
    }

    /// <summary>Navigates to the followers of an account that the signed-in account also follows.</summary>
    public static void OpenKnownFollowers(string actor, int knownCount)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return;
        NavigationService.Instance.Navigate(typeof(FollowListPage), new FollowListArgs(actor, false, knownCount, 0, KnownOnly: true));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is FollowListArgs args)
        {
            if (args.ShowFollowing)
                FollowingTab.IsSelected = true;
            Tabs.Visibility = args.KnownOnly ? Visibility.Collapsed : Visibility.Visible;
            _ = ViewModel.LoadAsync(args);
        }
    }

    private bool ShowingFollowing => Tabs.SelectedItem == FollowingTab;

    private ObservableCollection<ActorItem> SelectedList => ShowingFollowing ? ViewModel.Following : ViewModel.Followers;

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowSelectedList();

    private void ShowSelectedList()
    {
        // IsSelected in XAML can raise SelectionChanged mid-InitializeComponent, before the list exists.
        if (PeopleList is null || EmptyText is null)
            return;

        PeopleList.ItemsSource = SelectedList;
        _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
        UpdateEmptyText();
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyText();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FollowListViewModel.IsLoading) or nameof(FollowListViewModel.HasError))
            UpdateEmptyText();
    }

    private void UpdateEmptyText()
    {
        EmptyText.Text = ViewModel.KnownOnly
            ? "None of the people you follow follow this account"
            : ShowingFollowing ? "Not following anyone yet" : "No followers yet";
        EmptyText.Visibility = !ViewModel.IsLoading && !ViewModel.HasError && SelectedList.Count == 0
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
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(PeopleList);
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += OnViewChanged;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollViewer is null || e.IsIntermediate)
            return;

        double remaining = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        if (remaining < _scrollViewer.ViewportHeight * 2)
            _ = ShowingFollowing ? ViewModel.LoadMoreFollowingAsync() : ViewModel.LoadMoreFollowersAsync();
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

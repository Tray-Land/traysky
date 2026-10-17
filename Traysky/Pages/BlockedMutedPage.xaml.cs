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
/// The accounts you've blocked and muted, one tab each, with an unblock / unmute button per row.
/// Reached from Settings; tapping a person opens their profile.
/// </summary>
public sealed partial class BlockedMutedPage : Page
{
    private ScrollViewer? _scrollViewer;

    public BlockedMutedViewModel ViewModel { get; } = new();

    public BlockedMutedPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        ViewModel.Blocked.CollectionChanged += OnListChanged;
        ViewModel.Muted.CollectionChanged += OnListChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ShowSelectedList();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    private bool ShowingMuted => Tabs.SelectedItem == MutedTab;

    private ObservableCollection<ActorItem> SelectedList => ShowingMuted ? ViewModel.Muted : ViewModel.Blocked;

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
        if (e.PropertyName is nameof(BlockedMutedViewModel.IsLoading) or nameof(BlockedMutedViewModel.HasError))
            UpdateEmptyText();
    }

    private void UpdateEmptyText()
    {
        EmptyText.Text = ManagedAccountsPolicy.EmptyText(blocks: !ShowingMuted);
        EmptyText.Visibility = !ViewModel.IsLoading && !ViewModel.HasError && SelectedList.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PeopleList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem actor })
            _ = ViewModel.RemoveAsync(actor, blocks: !ShowingMuted);
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
            _ = ShowingMuted ? ViewModel.LoadMoreMutedAsync() : ViewModel.LoadMoreBlockedAsync();
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

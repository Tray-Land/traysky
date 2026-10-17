using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Specialized;
using System.ComponentModel;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// The accounts whose bell is on, with a "Turn off" button per row. Reached from Settings;
/// tapping a person opens their profile.
/// </summary>
public sealed partial class ActivitySubscriptionsPage : Page
{
    private ScrollViewer? _scrollViewer;

    public ActivitySubscriptionsViewModel ViewModel { get; } = new();

    public ActivitySubscriptionsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        ViewModel.Accounts.CollectionChanged += OnListChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyText();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ActivitySubscriptionsViewModel.IsLoading) or nameof(ActivitySubscriptionsViewModel.HasError))
            UpdateEmptyText();
    }

    private void UpdateEmptyText() =>
        EmptyText.Visibility = !ViewModel.IsLoading && !ViewModel.HasError && ViewModel.Accounts.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void PeopleList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    private void TurnOff_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem actor })
            _ = ViewModel.TurnOffAsync(actor);
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
            _ = ViewModel.LoadMoreAsync();
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

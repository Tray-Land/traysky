using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// The newest posts for a hashtag. Navigation parameter is the tag, with or without the #.
/// </summary>
public sealed partial class HashtagPage : Page
{
    private ScrollViewer? _scrollViewer;

    public HashtagViewModel ViewModel { get; } = new();

    public HashtagPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Navigates to a hashtag feed. Used by the tags in post text.</summary>
    public static void Open(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;
        NavigationService.Instance.Navigate(typeof(HashtagPage), tag.Trim().TrimStart('#'));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string tag)
            _ = ViewModel.LoadAsync(tag);
    }

    public Style FollowButtonStyle(bool following) =>
        (Style)Application.Current.Resources[following ? "DefaultButtonStyle" : "AccentButtonStyle"];

    private void PostCard_ThreadRequested(object? sender, PostItem post) => PostPage.Open(post);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(PostList);
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += OnViewChanged;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollViewer is null || e.IsIntermediate)
            return;

        double remaining = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        if (remaining < _scrollViewer.ViewportHeight * 2 && ViewModel.HasMore)
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

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// The accounts in a starter pack. Navigation parameter is a <see cref="StarterPackArgs"/>;
/// tapping a person opens their profile.
/// </summary>
public sealed partial class StarterPackPage : Page
{
    private ScrollViewer? _scrollViewer;

    public StarterPackViewModel ViewModel { get; } = new();

    public StarterPackPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Navigates to a starter pack's member list.</summary>
    public static void Open(StarterPackItem pack)
    {
        if (pack.ListUri.Length == 0 && pack.AtUri.Length == 0 && !PreviewMode.IsEnabled)
            return;
        NavigationService.Instance.Navigate(
            typeof(StarterPackPage),
            new StarterPackArgs(pack.ListUri, pack.Name, pack.Description, pack.CreatorHandle, pack.WebUrl, pack.AtUri));
    }

    /// <summary>Opens a pack from its AT URI alone (a pasted link or deep link); the page fetches the rest.</summary>
    public static void OpenUri(string atUri)
    {
        if (atUri.Length == 0)
            return;
        NavigationService.Instance.Navigate(
            typeof(StarterPackPage),
            new StarterPackArgs(string.Empty, "Starter pack", string.Empty, string.Empty, string.Empty, atUri));
    }

    /// <summary>
    /// Opens the pack when <paramref name="link"/> is a starter-pack link (bsky.app or AT URI) and returns
    /// true; false leaves the caller to treat it as an ordinary link or query.
    /// </summary>
    public static bool TryOpenLink(string? link)
    {
        string? atUri = StarterPackPolicy.AtUriFromLink(link);
        if (atUri is null)
            return false;

        OpenUri(atUri);
        return true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is StarterPackArgs args)
            _ = ViewModel.LoadAsync(args);
    }

    private void MemberList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(MemberList);
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

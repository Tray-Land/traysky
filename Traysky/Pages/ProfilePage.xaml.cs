using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>Navigation parameter that puts <see cref="ProfilePage"/> in its contained mode, for <paramref name="Actor"/> (a DID or handle).</summary>
public sealed record ContainedProfileRequest(string Actor);

/// <summary>
/// An account's profile and recent posts. Navigation parameter is a DID or handle.
/// </summary>
public sealed partial class ProfilePage : Page
{
    /// <summary>
    /// True when the page is hosted in its own window's frame (see
    /// <see cref="Controls.ProfilePreviewWindow"/>) rather than the tray popup's. Set by
    /// navigating with a <see cref="ContainedProfileRequest"/>. A contained page must never reach
    /// the popup's <see cref="NavigationService"/>, the shared compose draft or the popup's
    /// overlays, so its post cards are contained too (see <see cref="Controls.PostCard.IsContained"/>),
    /// it hides the actions that would (messages, lists, settings, thread and follower pages,
    /// starter packs) and sends the rest to bsky.app or to its own frame.
    /// </summary>
    public static readonly DependencyProperty IsContainedProperty =
        DependencyProperty.Register(nameof(IsContained), typeof(bool), typeof(ProfilePage), new PropertyMetadata(false));

    public bool IsContained
    {
        get => (bool)GetValue(IsContainedProperty);
        private set => SetValue(IsContainedProperty, value);
    }

    /// <summary>x:Bind can't nest function calls, so this folds "show unless contained" into one.</summary>
    public Visibility VisibleUnlessContained(bool value, bool contained) =>
        value && !contained ? Visibility.Visible : Visibility.Collapsed;

    private ScrollViewer? _scrollViewer;

    public ProfileViewModel ViewModel { get; } = new();

    public ProfilePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private ProfileTab SelectedBarTab =>
        Tabs.SelectedItem == LikesTab ? ProfileTab.Likes
        : Tabs.SelectedItem == FeedsTab ? ProfileTab.Feeds
        : Tabs.SelectedItem == PacksTab ? ProfileTab.StarterPacks
        : Tabs.SelectedItem == ListsTab ? ProfileTab.Lists
        : ProfileTab.Posts;

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // IsSelected in XAML can raise SelectionChanged mid-InitializeComponent, before the list exists.
        if (PostList is null)
            return;

        _ = ViewModel.SelectTabAsync(SelectedBarTab);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProfileViewModel.SelectedTab))
            return;

        // Swapping the source resets the scroll position; put it back so the tab bar stays where it was.
        double offset = _scrollViewer?.VerticalOffset ?? 0;
        PostList.ItemsSource = ViewModel.ItemsFor(ViewModel.SelectedTab);
        if (_scrollViewer is not null && offset > 0)
        {
            PostList.UpdateLayout();
            _scrollViewer.ChangeView(null, offset, null, disableAnimation: true);
        }

        // The view model may have moved off a tab the profile doesn't offer (Likes on someone else's).
        if (SelectedBarTab != ViewModel.SelectedTab)
        {
            Tabs.SelectedItem = ViewModel.SelectedTab switch
            {
                ProfileTab.Likes => LikesTab,
                ProfileTab.Feeds => FeedsTab,
                ProfileTab.StarterPacks => PacksTab,
                ProfileTab.Lists => ListsTab,
                _ => PostsTab
            };
        }
    }

    private void FeedRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FeedSuggestionItem feed })
            _ = RichTextBuilder.OpenAsync(feed.WebUrl);
    }

    // The button sits inside a row that opens the feed when tapped; pressing Pin shouldn't also do that.
    private void PinFeed_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private async void PinFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FeedSuggestionItem feed })
            await Helpers.FeedPinning.ToggleAsync(feed, XamlRoot);
    }

    private void PackRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StarterPackItem pack })
            StarterPackPage.Open(pack);
    }

    private void ListRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ListItem list })
            ListPage.Open(list);
    }

    private async void NewList_Click(object sender, RoutedEventArgs e)
    {
        var answer = await Helpers.ListDialogs.AskListAsync(XamlRoot, existing: null);
        if (answer is not { } created)
            return;

        ListItem? list = await ViewModel.CreateListAsync(created.Name, created.Description, created.Kind);
        if (list is null)
        {
            await Helpers.ModerationDialogs.ShowMessageAsync(XamlRoot, "Couldn't create the list", "That didn't go through. Try again in a moment.");
            return;
        }

        ListPage.Open(list);
    }

    private async void AddToList_Click(object sender, RoutedEventArgs e) =>
        await Helpers.ListDialogs.ShowMembershipAsync(XamlRoot, ViewModel.ActorKey, ViewModel.Handle);

    private void ContainedPost_ProfileRequested(object? sender, string did) =>
        Frame?.Navigate(typeof(ProfilePage), new ContainedProfileRequest(did));

    private void SimilarRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ActorItem actor })
            return;

        // A contained page keeps going inside its own frame, so its window gets a back stack.
        if (IsContained)
            Frame?.Navigate(typeof(ProfilePage), new ContainedProfileRequest(actor.ProfileKey.TrimStart('@')));
        else
            Open(actor.ProfileKey);
    }

    private void KnownFollowers_Click(object sender, RoutedEventArgs e) =>
        FollowListPage.OpenKnownFollowers(ViewModel.ActorKey, ViewModel.KnownFollowersCount);

    private void SimilarExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args) => _ = ViewModel.LoadSimilarAsync();

    /// <summary>Navigates to a profile. Shared by avatars, "View profile", mentions and follow notifications.</summary>
    public static void Open(string didOrHandle)
    {
        if (string.IsNullOrWhiteSpace(didOrHandle))
            return;
        NavigationService.Instance.Navigate(typeof(ProfilePage), didOrHandle.Trim().TrimStart('@'));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ContainedProfileRequest request)
        {
            IsContained = true;

            // Before anything loads, so no row is ever realized as a full PostCard.
            ((Helpers.ProfileRowTemplateSelector)Resources["RowSelector"]).PostTemplate = (DataTemplate)Resources["ContainedPostRowTemplate"];
            PostList.ItemTemplateSelector = (Helpers.ProfileRowTemplateSelector)Resources["RowSelector"];
            _ = ViewModel.LoadAsync(request.Actor);
        }
        else if (e.Parameter is string actor)
        {
            _ = ViewModel.LoadAsync(actor);
        }
    }

    public bool AnyLoading(bool a, bool b) => a || b;

    public Style FollowButtonStyle(bool following) =>
        (Style)Application.Current.Resources[following ? "DefaultButtonStyle" : "AccentButtonStyle"];

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => NavigationService.Instance.Navigate(typeof(SettingsPage));

    private void Bell_Opening(object? sender, object e)
    {
        BellOffItem.IsChecked = ViewModel.ActivityLevel == ActivitySubscriptionLevel.Off;
        BellPostsItem.IsChecked = ViewModel.ActivityLevel == ActivitySubscriptionLevel.Posts;
        BellRepliesItem.IsChecked = ViewModel.ActivityLevel == ActivitySubscriptionLevel.PostsAndReplies;
    }

    private async void Bell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !System.Enum.TryParse(tag, out ActivitySubscriptionLevel level))
            return;

        if (!await ViewModel.SetActivityAsync(level))
            await Helpers.ModerationDialogs.ShowMessageAsync(XamlRoot, "Couldn't update notifications", "That didn't go through. Try again in a moment.");
    }

    private async void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (!await ViewModel.ToggleMuteAsync())
            await Helpers.ModerationDialogs.ShowMessageAsync(XamlRoot, "Couldn't update mute", "That didn't go through. Try again in a moment.");
    }

    private async void Block_Click(object sender, RoutedEventArgs e)
    {
        // Unblocking needs no confirmation.
        if (!ViewModel.IsBlocking && !await Helpers.ModerationDialogs.ConfirmBlockAsync(XamlRoot, ViewModel.Handle))
            return;

        if (!await ViewModel.ToggleBlockAsync())
            await Helpers.ModerationDialogs.ShowMessageAsync(XamlRoot, "Couldn't update block", "That didn't go through. Try again in a moment.");
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        var answer = await Helpers.ModerationDialogs.AskReportAsync(XamlRoot, "account");
        if (answer is not { } report)
            return;

        bool sent = await ViewModel.ReportAsync(report.Reason, report.Details);
        await Helpers.ModerationDialogs.ShowMessageAsync(XamlRoot,
            sent ? "Report sent" : "Couldn't send report",
            sent ? "Thanks. Bluesky's moderators will take a look." : "The report didn't go through. Try again in a moment.");
    }

    private void BookmarksButton_Click(object sender, RoutedEventArgs e) => BookmarksPage.Open();

    private void FollowersButton_Click(object sender, RoutedEventArgs e) => OpenFollowList(showFollowing: false);

    private void FollowingButton_Click(object sender, RoutedEventArgs e) => OpenFollowList(showFollowing: true);

    private void OpenFollowList(bool showFollowing)
    {
        if (IsContained)
        {
            _ = RichTextBuilder.OpenAsync($"https://bsky.app/profile/{ViewModel.ActorKey}/{(showFollowing ? "follows" : "followers")}");
            return;
        }

        FollowListPage.Open(ViewModel.ActorKey, showFollowing, ViewModel.FollowersCount, ViewModel.FollowsCount);
    }

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

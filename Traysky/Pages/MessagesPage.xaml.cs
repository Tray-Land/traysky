using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// The account's direct message conversations, reached from the title bar's Messages button
/// (only shown when the session has DM access - see <see cref="Services.ChatService"/>).
/// </summary>
public sealed partial class MessagesPage : Page
{
    private ScrollViewer? _scrollViewer;

    public MessagesViewModel ViewModel { get; } = MessagesViewModel.Instance;

    public MessagesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshIfStaleAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(ConversationList);
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

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // IsSelected in XAML can raise SelectionChanged mid-InitializeComponent, before the tabs exist.
        if (RequestsTab is null)
            return;

        ChatListMode mode = sender.SelectedItem == RequestsTab ? ChatListMode.Requests : ChatListMode.All;
        _ = ViewModel.SetModeAsync(mode);
    }

    private void ConversationList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ConversationItem conversation)
            ConversationPage.Open(conversation);
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ConversationItem conversation })
            ViewModel.AcceptRequestCommand.Execute(conversation);
    }

    private async void Decline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConversationItem conversation })
            return;

        if (await Helpers.ChatDialogs.ConfirmAsync(XamlRoot, "Decline this request?", $"The request from {conversation.Title} is removed.", "Decline"))
            ViewModel.DeclineRequestCommand.Execute(conversation);
    }

    private async void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        if (await Helpers.GroupDialogs.AskNewGroupAsync(XamlRoot) is not { } answer)
            return;

        ConversationItem? created;
        if (PreviewMode.IsEnabled)
        {
            created = PreviewMode.SampleNewGroup(answer.Name, answer.Members.Count);
        }
        else
        {
            (created, string? error) = await GroupChatService.CreateAsync(answer.Name, answer.Members.Select(m => m.Did).ToList());
            if (error is not null)
            {
                await Helpers.ModerationDialogs.ShowMessageAsync(XamlRoot, "Couldn't create the group", error);
                return;
            }
        }

        if (created is null)
        {
            // The group exists but the server didn't hand it back; the list will show it.
            ViewModel.Invalidate();
            _ = ViewModel.RefreshAsync();
            return;
        }

        ViewModel.BringToTop(created);
        ConversationPage.Open(created);
    }

    private async void JoinGroup_Click(object sender, RoutedEventArgs e) => await Helpers.GroupDialogs.JoinAsync(XamlRoot);

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

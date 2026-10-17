using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using Traysky.Helpers;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.ApplicationModel.DataTransfer;

namespace Traysky.Pages;

/// <summary>
/// A group chat's details: who is in it, and for the owner the controls to rename it, add and
/// remove people and run the join link. Navigation parameter is the group's <see cref="ConversationItem"/>.
/// </summary>
public sealed partial class GroupInfoPage : Page
{
    private ScrollViewer? _scrollViewer;

    public GroupInfoViewModel ViewModel { get; } = new();

    public GroupInfoPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public static void Open(ConversationItem group) =>
        NavigationService.Instance.Navigate(typeof(GroupInfoPage), group);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ConversationItem group && ViewModel.ConversationId is null)
            _ = ViewModel.LoadAsync(group);
    }

    private void MemberList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (await GroupDialogs.AskNameAsync(XamlRoot, ViewModel.Name) is { } name)
            await ViewModel.RenameAsync(name);
    }

    private async void AddPeople_Click(object sender, RoutedEventArgs e)
    {
        int room = GroupChatPolicy.RoomLeft((int)ViewModel.MemberCount, (int)ViewModel.MemberLimit);
        if (room == 0 && ViewModel.MemberLimit > 0)
        {
            await ModerationDialogs.ShowMessageAsync(XamlRoot, "This group is full", $"A group holds at most {ViewModel.MemberLimit} people.");
            return;
        }

        List<ActorItem>? people = await GroupDialogs.AskMembersAsync(XamlRoot, room == 0 ? int.MaxValue : room, ViewModel.MemberDids);
        if (people is not null)
            await ViewModel.AddAsync(people);
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ActorItem member })
            return;

        if (await ChatDialogs.ConfirmAsync(XamlRoot, $"Remove {member.NameOrHandle}?", "They are taken out of the group and can't read new messages.", "Remove"))
            await ViewModel.RemoveAsync(member);
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem person })
            _ = ViewModel.ApproveAsync(person);
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem person })
            _ = ViewModel.RejectAsync(person);
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new();
        package.SetText(ViewModel.LinkCode);
        Clipboard.SetContent(package);
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

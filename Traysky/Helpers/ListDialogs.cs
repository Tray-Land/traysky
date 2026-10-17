using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.Helpers;

/// <summary>The dialogs behind list management: make or edit a list, delete one, and the "Add to list" checkboxes.</summary>
public static class ListDialogs
{
    /// <summary>
    /// Asks for a list's name, description and (for a new list) purpose. Null when cancelled. The purpose is
    /// fixed once a list exists, so editing shows it but doesn't offer to change it.
    /// </summary>
    public static async Task<(string Name, string Description, ListKind Kind)?> AskListAsync(XamlRoot root, ListItem? existing)
    {
        ListKind[] kinds = [ListKind.Curate, ListKind.Moderation];

        RadioButtons purpose = new() { SelectedIndex = existing?.Kind == ListKind.Moderation ? 1 : 0, IsEnabled = existing is null };
        foreach (ListKind kind in kinds)
            purpose.Items.Add($"{ListPolicy.KindLabel(kind)}: {ListPolicy.KindHint(kind)}");

        TextBox name = new() { Header = "Name", Text = existing?.Name ?? string.Empty, MaxLength = ListPolicy.NameMaxBytes };
        TextBox description = new()
        {
            Header = "Description (optional)",
            Text = existing?.Description ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 88
        };

        StackPanel panel = new() { Spacing = 12 };
        panel.Children.Add(name);
        panel.Children.Add(description);
        panel.Children.Add(purpose);

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = existing is null ? "New list" : "Edit list",
            Content = panel,
            PrimaryButtonText = existing is null ? "Create" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = ListPolicy.CanSave(name.Text, description.Text)
        };

        void Revalidate(object? sender, object e) => dialog.IsPrimaryButtonEnabled = ListPolicy.CanSave(name.Text, description.Text);
        name.TextChanged += (s, e) => Revalidate(s, e);
        description.TextChanged += (s, e) => Revalidate(s, e);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return (ListPolicy.NormalizeName(name.Text) ?? string.Empty, description.Text.Trim(), kinds[System.Math.Max(0, purpose.SelectedIndex)]);
    }

    public static async Task<bool> ConfirmDeleteAsync(XamlRoot root, string name)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = $"Delete {name}?",
            Content = "The list and its members are removed. This can't be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<bool> ConfirmBlockAsync(XamlRoot root, string name)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = $"Block everyone on {name}?",
            Content = ListPolicy.BlockConfirmText(name),
            PrimaryButtonText = "Block list",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// "Add to list": one checkbox per list you own, ticked where <paramref name="did"/> is already a member.
    /// Each tick applies at once and unticks itself if the server refuses.
    /// </summary>
    public static async Task ShowMembershipAsync(XamlRoot root, string did, string handle)
    {
        StackPanel panel = new() { Spacing = 4, MinWidth = 280 };
        ProgressRing spinner = new() { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(spinner);

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = $"Add @{handle} to a list",
            Content = new ScrollViewer { Content = panel, MaxHeight = 360 },
            CloseButtonText = "Done"
        };

        Task shown = dialog.ShowAsync().AsTask();

        List<ListMembershipItem>? lists = PreviewMode.IsEnabled ? PreviewMode.SampleMemberships() : await ListsService.GetMembershipsAsync(did);
        panel.Children.Clear();

        if (lists is null)
            panel.Children.Add(new TextBlock { Text = "Couldn't load your lists. Try again in a moment.", TextWrapping = TextWrapping.Wrap });
        else if (lists.Count == 0)
            panel.Children.Add(new TextBlock { Text = "You don't have any lists yet. Make one from your profile's Lists tab.", TextWrapping = TextWrapping.Wrap });

        TextBlock message = new() { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

        foreach (ListMembershipItem entry in lists ?? [])
        {
            CheckBox box = new() { Content = $"{entry.List.Name}  ({ListPolicy.KindLabel(entry.List.Kind).ToLowerInvariant()})", IsChecked = entry.IsMember };
            box.Click += async (_, _) =>
            {
                bool wanted = box.IsChecked == true;
                MembershipChange change = ListPolicy.Change(entry.IsMember, wanted);
                if (change == MembershipChange.None || entry.IsBusy)
                    return;

                entry.IsBusy = true;
                box.IsEnabled = false;
                message.Visibility = Visibility.Collapsed;

                bool ok;
                if (PreviewMode.IsEnabled)
                {
                    ok = true;
                    entry.MembershipUri = wanted ? "at://preview" : null;
                }
                else if (change == MembershipChange.Add)
                {
                    entry.MembershipUri = await ListsService.AddMemberAsync(entry.List.Uri, did);
                    ok = entry.MembershipUri is not null;
                }
                else
                {
                    ok = entry.MembershipUri is not null && await ListsService.RemoveMemberAsync(entry.MembershipUri);
                    if (ok)
                        entry.MembershipUri = null;
                }

                if (ok)
                    entry.IsMember = wanted;
                else
                {
                    box.IsChecked = entry.IsMember;
                    message.Text = ListPolicy.MembershipFailureText(change, entry.List.Name);
                    message.Visibility = Visibility.Visible;
                }

                box.IsEnabled = true;
                entry.IsBusy = false;
            };
            panel.Children.Add(box);
        }

        panel.Children.Add(message);
        await shown;
    }
}

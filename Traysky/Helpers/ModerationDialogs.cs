using idunno.Bluesky.Moderation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Traysky.Helpers;

/// <summary>The confirmation / reason / failure dialogs that go with mute, block and report.</summary>
public static class ModerationDialogs
{
    public static async Task<bool> ConfirmDetachQuoteAsync(XamlRoot root, string handle)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = "Detach your post from this quote?",
            Content = $"@{handle}'s post will no longer show your post. This can't be undone from Traysky.",
            PrimaryButtonText = "Detach",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<bool> ConfirmBlockAsync(XamlRoot root, string handle)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = $"Block @{handle}?",
            Content = "They won't be able to see your posts or follow you, and you won't see theirs. You can unblock them later from their profile.",
            PrimaryButtonText = "Block",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<bool> ConfirmDeletePostAsync(XamlRoot root)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = "Delete this post?",
            Content = "This can't be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Asks why; null when cancelled.</summary>
    public static async Task<(ReportType Reason, string Details)?> AskReportAsync(XamlRoot root, string what)
    {
        (string Label, ReportType Type)[] options =
        [
            ("Spam", ReportType.Spam),
            ("Misleading", ReportType.Misleading),
            ("Rude or harassing", ReportType.Rude),
            ("Sexual content", ReportType.Sexual),
            ("Breaks the law or Bluesky's rules", ReportType.Violation),
            ("Something else", ReportType.Other)
        ];

        RadioButtons choices = new() { SelectedIndex = 0 };
        foreach ((string label, _) in options)
            choices.Items.Add(label);

        TextBox details = new() { PlaceholderText = "Anything else the moderators should know (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72 };

        StackPanel panel = new() { Spacing = 12 };
        panel.Children.Add(choices);
        panel.Children.Add(details);

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = $"Report {what}",
            Content = panel,
            PrimaryButtonText = "Report",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return (options[System.Math.Max(0, choices.SelectedIndex)].Type, details.Text.Trim());
    }

    public static async Task ShowMessageAsync(XamlRoot root, string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }
}

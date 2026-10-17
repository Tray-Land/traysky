using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.Helpers;

/// <summary>The reply and quote controls dialog, used by the compose box (before posting) and the post menu (after).</summary>
public static class GateDialogs
{
    /// <summary>
    /// Asks who may reply and whether the post can be quoted. Pass a null <paramref name="reply"/> to leave out the
    /// reply section (a reply isn't a thread root, so it has none). Null when cancelled.
    /// </summary>
    public static async Task<(ReplyChoice? Reply, bool AllowQuotes)?> AskAsync(
        XamlRoot root, ReplyChoice? reply, bool allowQuotes, IReadOnlyList<ListItem> lists)
    {
        StackPanel panel = new() { Spacing = 12, MinWidth = 300 };

        RadioButtons audience = new() { Header = "Who can reply" };
        audience.Items.Add("Everyone");
        audience.Items.Add("Nobody");
        audience.Items.Add("Only some people");

        CheckBox mentioned = new() { Content = "People you mention" };
        CheckBox followers = new() { Content = "Your followers" };
        CheckBox following = new() { Content = "People you follow" };
        List<(CheckBox Box, ListItem List)> listBoxes = lists.Select(l => (new CheckBox { Content = $"List: {l.Name}" }, l)).ToList();

        StackPanel groups = new() { Spacing = 2, Margin = new Thickness(24, 0, 0, 0) };
        groups.Children.Add(mentioned);
        groups.Children.Add(followers);
        groups.Children.Add(following);
        foreach ((CheckBox box, _) in listBoxes)
            groups.Children.Add(box);

        CheckBox quotes = new() { Content = "Allow quote posts", IsChecked = allowQuotes };

        if (reply is not null)
        {
            ReplyChoice shown = InteractionGatePolicy.Normalize(reply);
            audience.SelectedIndex = (int)shown.Audience;
            mentioned.IsChecked = shown.Mentioned;
            followers.IsChecked = shown.Followers;
            following.IsChecked = shown.Following;
            foreach ((CheckBox box, ListItem list) in listBoxes)
                box.IsChecked = shown.Lists.Contains(list.Uri);

            panel.Children.Add(audience);
            panel.Children.Add(groups);
        }

        panel.Children.Add(quotes);

        ReplyChoice Current()
        {
            ReplyAudience chosen = audience.SelectedIndex switch
            {
                1 => ReplyAudience.Nobody,
                2 => ReplyAudience.Limited,
                _ => ReplyAudience.Everyone
            };

            return new ReplyChoice(
                chosen,
                mentioned.IsChecked == true,
                followers.IsChecked == true,
                following.IsChecked == true,
                listBoxes.Where(p => p.Box.IsChecked == true).Select(p => p.List.Uri).ToList());
        }

        // The rule limit is shared by the groups and the lists; once it's reached, only unticking is possible.
        void Refresh()
        {
            ReplyChoice choice = Current();
            groups.Visibility = choice.Audience == ReplyAudience.Limited ? Visibility.Visible : Visibility.Collapsed;

            bool room = InteractionGatePolicy.CanTickList(choice);
            foreach (CheckBox box in groups.Children.OfType<CheckBox>())
                box.IsEnabled = room || box.IsChecked == true;
        }

        audience.SelectionChanged += (_, _) => Refresh();
        foreach (CheckBox box in groups.Children.OfType<CheckBox>())
        {
            box.Checked += (_, _) => Refresh();
            box.Unchecked += (_, _) => Refresh();
        }
        Refresh();

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = reply is null ? "Quote settings" : "Who can interact",
            Content = new ScrollViewer { Content = panel, MaxHeight = 440 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return (reply is null ? null : InteractionGatePolicy.Normalize(Current()), quotes.IsChecked != false);
    }

    /// <summary>
    /// The post menu's "Reply and quote settings": loads the post's current gates, asks, and writes only what changed.
    /// True when nothing needed saving or the save went through; false when loading or saving failed.
    /// </summary>
    public static async Task<bool> EditPostAsync(XamlRoot root, PostItem post)
    {
        bool isRoot = string.Equals(post.ThreadRootUri, post.AtUri, System.StringComparison.Ordinal);
        bool includeReply = InteractionGatePolicy.CanEditReplySettings(own: true, isThreadRoot: isRoot);

        GatesService.State? state;
        IReadOnlyList<ListItem> lists = [];
        if (PreviewMode.IsEnabled)
            state = new GatesService.State(includeReply ? ReplyChoice.Everyone : null, AllowQuotes: true);
        else
        {
            state = await GatesService.LoadAsync(post.AtUri, includeReply);
            if (state is null)
                return false;

            if (includeReply)
                lists = await ListsService.GetOwnCurationListsAsync() ?? [];
        }

        var answer = await AskAsync(root, state.Reply, state.AllowQuotes, lists);
        if (answer is not { } chosen)
            return true;

        bool replyChanged = chosen.Reply is not null && state.Reply is not null && !InteractionGatePolicy.AreSame(chosen.Reply, state.Reply);
        bool quotesChanged = chosen.AllowQuotes != state.AllowQuotes;
        if (PreviewMode.IsEnabled)
            return true;

        bool ok = true;
        if (replyChanged)
            ok &= await GatesService.SetReplyChoiceAsync(post.AtUri, chosen.Reply!);
        if (quotesChanged)
            ok &= await GatesService.SetAllowQuotesAsync(post.AtUri, chosen.AllowQuotes);
        return ok;
    }

    /// <summary>Asks what to do with a draft that was just restored over text already in the box; true to replace it.</summary>
    public static async Task<bool> ConfirmReplaceDraftAsync(XamlRoot root)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = "Replace what you've written?",
            Content = "Restoring this draft replaces the text in the compose box.",
            PrimaryButtonText = "Replace",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

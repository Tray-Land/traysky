using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Pages;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Maximum = idunno.Bluesky.Maximum;

namespace Traysky.Helpers;

/// <summary>The dialogs behind group chats: start one, add people, rename, and join by link.</summary>
public static class GroupDialogs
{
    private static readonly int MemberLimit = Maximum.GroupMembers;

    // ---- Start a group, add people -------------------------------------------------------

    /// <summary>Asks for a name and the people to start a group with. Null when cancelled.</summary>
    public static async Task<(string Name, List<ActorItem> Members)?> AskNewGroupAsync(XamlRoot root)
    {
        TextBox name = new() { Header = "Group name", MaxLength = Maximum.GroupNameLengthInGraphemes };
        PeoplePicker picker = new(MemberLimit - 1, new HashSet<string>(StringComparer.Ordinal));

        StackPanel panel = new() { Spacing = 12, MinWidth = 300 };
        panel.Children.Add(name);
        panel.Children.Add(picker.View);

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = "New group",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        void Revalidate() => dialog.IsPrimaryButtonEnabled = GroupChatPolicy.CanCreate(
            name.Text, picker.Chosen.Count, Maximum.GroupNameLengthInGraphemes, Maximum.GroupNameLengthInBytes, MemberLimit);
        name.TextChanged += (_, _) => Revalidate();
        picker.Changed += Revalidate;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return (GroupChatPolicy.NormalizeName(name.Text, Maximum.GroupNameLengthInGraphemes, Maximum.GroupNameLengthInBytes) ?? name.Text.Trim(), picker.Chosen.ToList());
    }

    /// <summary>Asks who to add to a group that has room for <paramref name="roomLeft"/> more. Null when cancelled or nobody was picked.</summary>
    public static async Task<List<ActorItem>?> AskMembersAsync(XamlRoot root, int roomLeft, IReadOnlySet<string> alreadyIn)
    {
        PeoplePicker picker = new(roomLeft, alreadyIn);

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = "Add people",
            Content = picker.View,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };
        picker.Changed += () => dialog.IsPrimaryButtonEnabled = picker.Chosen.Count > 0;

        return await dialog.ShowAsync() == ContentDialogResult.Primary && picker.Chosen.Count > 0 ? picker.Chosen.ToList() : null;
    }

    /// <summary>Asks for a group's new name. Null when cancelled or unchanged.</summary>
    public static async Task<string?> AskNameAsync(XamlRoot root, string current)
    {
        TextBox name = new() { Header = "Group name", Text = current, MaxLength = Maximum.GroupNameLengthInGraphemes };
        name.SelectAll();

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = "Rename group",
            Content = name,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        bool Valid() => GroupChatPolicy.NormalizeName(name.Text, Maximum.GroupNameLengthInGraphemes, Maximum.GroupNameLengthInBytes) is { } trimmed
            && !string.Equals(trimmed, current, StringComparison.Ordinal);
        dialog.IsPrimaryButtonEnabled = Valid();
        name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = Valid();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return GroupChatPolicy.NormalizeName(name.Text, Maximum.GroupNameLengthInGraphemes, Maximum.GroupNameLengthInBytes);
    }

    // ---- Join by link --------------------------------------------------------------------

    /// <summary>
    /// The whole "join a group" flow: ask for a link (unless <paramref name="code"/> came from a deep
    /// link), show what is behind it, join or request to join, and open the group if the account is now in.
    /// </summary>
    public static async Task JoinAsync(XamlRoot root, string? code = null)
    {
        if (code is null)
        {
            TextBox box = new() { Header = "Join link or code", PlaceholderText = "Paste the group's join link" };
            ContentDialog ask = new()
            {
                XamlRoot = root,
                Title = "Join a group",
                Content = box,
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false
            };
            box.TextChanged += (_, _) => ask.IsPrimaryButtonEnabled = GroupChatPolicy.ParseJoinCode(box.Text) is not null;

            if (await ask.ShowAsync() != ContentDialogResult.Primary)
                return;

            code = GroupChatPolicy.ParseJoinCode(box.Text);
            if (code is null)
                return;
        }

        GroupJoinPreview? preview = PreviewMode.IsEnabled ? PreviewMode.SampleJoinPreview(code) : await GroupChatService.PreviewAsync(code);
        if (preview is null)
        {
            await ModerationDialogs.ShowMessageAsync(root, "Couldn't check that link", "Couldn't reach Bluesky. Try again in a moment.");
            return;
        }

        if (preview.IsInvalid || preview.IsDisabled)
        {
            await ModerationDialogs.ShowMessageAsync(root, "That link doesn't work",
                preview.IsDisabled ? "The group's owner has turned this join link off." : "That isn't a group join link, or it no longer exists.");
            return;
        }

        if (preview.AlreadyIn is { } mine)
        {
            ConversationPage.Open(mine);
            return;
        }

        if (preview.RequestPending)
        {
            await OfferWithdrawAsync(root, preview);
            return;
        }

        string who = preview.OwnerName.Length > 0 ? $"Made by {preview.OwnerName}. " : string.Empty;
        string detail = GroupChatPolicy.JoinLinkSummary(true, preview.RequireApproval, preview.FollowedByOwnerOnly);
        ContentDialog confirm = new()
        {
            XamlRoot = root,
            Title = preview.Name.Length > 0 ? preview.Name : "Join group",
            Content = $"{who}{GroupChatPolicy.MemberCountText(preview.MemberCount, preview.MemberLimit)}.\n{detail}",
            PrimaryButtonText = GroupChatPolicy.JoinButtonText(preview.RequireApproval),
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (PreviewMode.IsEnabled)
        {
            await ModerationDialogs.ShowMessageAsync(root, preview.Name, GroupChatPolicy.JoinOutcomeText(!preview.RequireApproval, preview.Name));
            return;
        }

        (GroupJoinPreview? after, string? error) = await GroupChatService.JoinAsync(code);
        if (error is not null)
        {
            await ModerationDialogs.ShowMessageAsync(root, "Couldn't join", error);
            return;
        }

        if (after?.AlreadyIn is { } joined)
        {
            MessagesViewModel.Instance.BringToTop(joined);
            ConversationPage.Open(joined);
            return;
        }

        await ModerationDialogs.ShowMessageAsync(root, preview.Name, GroupChatPolicy.JoinOutcomeText(false, preview.Name));
    }

    /// <summary>Tells the user their request is waiting, and lets them cancel it when the preview says which group it is.</summary>
    private static async Task OfferWithdrawAsync(XamlRoot root, GroupJoinPreview preview)
    {
        const string waiting = "You've already asked to join. The owner has to approve it.";
        if (!GroupChatPolicy.CanWithdrawJoinRequest(preview.RequestPending, preview.ConversationId) || preview.ConversationId is not { } conversationId)
        {
            await ModerationDialogs.ShowMessageAsync(root, preview.Name, waiting);
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = preview.Name.Length > 0 ? preview.Name : "Join request",
            Content = waiting,
            PrimaryButtonText = "Cancel request",
            CloseButtonText = "Keep waiting",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (PreviewMode.IsEnabled)
            return;

        if (await GroupChatService.WithdrawJoinRequestAsync(conversationId) is { } error)
            await ModerationDialogs.ShowMessageAsync(root, "Couldn't cancel the request", error);
    }

    // ---- People picker -------------------------------------------------------------------

    /// <summary>
    /// A search box over Bluesky accounts and the people picked so far, as removable buttons. Built in code
    /// so both dialogs share it; suggestions go in as plain strings and are matched back to accounts.
    /// </summary>
    private sealed class PeoplePicker
    {
        private readonly int _room;
        private readonly IReadOnlySet<string> _exclude;
        private readonly AutoSuggestBox _box;
        private readonly StackPanel _chips = new() { Spacing = 4 };
        private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        private readonly Dictionary<string, ActorItem> _suggested = new(StringComparer.Ordinal);
        private CancellationTokenSource? _search;

        public PeoplePicker(int room, IReadOnlySet<string> exclude)
        {
            _room = room;
            _exclude = exclude;

            _box = new AutoSuggestBox { Header = "People", PlaceholderText = "Search by name or handle", QueryIcon = new SymbolIcon(Symbol.Find) };
            _box.TextChanged += async (s, e) =>
            {
                if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                    await SearchAsync(s);
            };
            _box.SuggestionChosen += (s, e) =>
            {
                if (e.SelectedItem is string label && _suggested.TryGetValue(label, out ActorItem? actor))
                    Add(actor);
                s.Text = string.Empty;
                s.ItemsSource = null;
            };

            StackPanel stack = new() { Spacing = 8, MinWidth = 300 };
            stack.Children.Add(_box);
            stack.Children.Add(_note);
            stack.Children.Add(new ScrollViewer { Content = _chips, MaxHeight = 200 });
            View = stack;
        }

        public FrameworkElement View { get; }

        public List<ActorItem> Chosen { get; } = [];

        public event Action? Changed;

        private void Add(ActorItem actor)
        {
            if (_exclude.Contains(actor.Did) || Chosen.Any(c => c.Did == actor.Did))
                return;

            if (Chosen.Count >= _room)
            {
                _note.Text = $"A group holds at most {MemberLimit} people, you included.";
                _note.Visibility = Visibility.Visible;
                return;
            }

            _note.Visibility = Visibility.Collapsed;
            Chosen.Add(actor);
            Rebuild();
        }

        private void Rebuild()
        {
            _chips.Children.Clear();
            foreach (ActorItem actor in Chosen.ToList())
            {
                Button chip = new() { Content = $"{actor.NameOrHandle}   ✕", HorizontalAlignment = HorizontalAlignment.Left };
                chip.Click += (_, _) =>
                {
                    Chosen.Remove(actor);
                    _note.Visibility = Visibility.Collapsed;
                    Rebuild();
                };
                _chips.Children.Add(chip);
            }
            Changed?.Invoke();
        }

        private async Task SearchAsync(AutoSuggestBox box)
        {
            _search?.Cancel();
            string query = box.Text.Trim();
            _suggested.Clear();
            if (query.Length < 2)
            {
                box.ItemsSource = null;
                return;
            }

            List<ActorItem> found = [];
            if (PreviewMode.IsEnabled)
            {
                found.AddRange(PreviewMode.SampleSearchActors());
            }
            else
            {
                CancellationTokenSource cts = _search = new CancellationTokenSource();
                try
                {
                    await Task.Delay(250, cts.Token);
                    var result = await BlueskySessionService.Instance.Agent.SearchActorsTypeahead(
                        query, limit: 6, subscribedLabelers: LabelService.Instance.Subscribed, cancellationToken: cts.Token);
                    if (cts.IsCancellationRequested || !result.Succeeded || result.Result is null)
                        return;

                    foreach (var profile in result.Result)
                        found.Add(FeedMapper.ToActorItem(profile));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogService.Warn("Chat", $"Group member search threw: {ex.Message}");
                    return;
                }
            }

            List<string> labels = [];
            foreach (ActorItem actor in found.Where(a => !_exclude.Contains(a.Did)))
            {
                string label = $"{actor.NameOrHandle}  {actor.HandleDisplay}";
                if (_suggested.TryAdd(label, actor))
                    labels.Add(label);
            }
            box.ItemsSource = labels.Count > 0 ? labels : null;
        }
    }
}

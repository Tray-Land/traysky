using CommunityToolkit.Common.Deferred;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Traysky.Controls;

/// <summary>
/// The compose box: the one on <see cref="Pages.ComposePage"/> (a fresh post, a quote, or a
/// reply to a post that isn't shown anywhere), or the one a PostCard realizes on its first
/// inline reply. One line until focused; Ctrl+Enter posts. The editor is a
/// RichSuggestBox (CommunityToolkit) so typing '@' or '#' opens a live suggestion popup; see
/// <see cref="RichSuggestTextPolicy"/> for how its token markup is turned back into the plain
/// "@handle"/"#tag" text <see cref="ComposeViewModel.Text"/> and PostAsync's facet extraction
/// already expect.
/// </summary>
public sealed partial class ComposeBox : UserControl
{
    public ComposeViewModel ViewModel { get; } = ComposeViewModel.Instance;

    /// <summary>Set while pushing ViewModel.Text into the editor, so the resulting TextChanged doesn't bounce back.</summary>
    private bool _syncingFromViewModel;

    /// <summary>Bumped per '@' suggestion request so a slow response can't overwrite a newer one's results.</summary>
    private int _mentionRequestEpoch;

    /// <summary>Held while this box is on screen with a draft in it, so the popup stays up while the user gathers content from other apps.</summary>
    private IDisposable? _lightDismissSuppression;

    /// <summary>
    /// True for the box on <see cref="Pages.ComposePage"/>: the whole page is for writing, so the
    /// editor is large and always expanded instead of collapsing when it loses focus, and a pin
    /// button is offered to keep the popup open.
    /// </summary>
    public static readonly DependencyProperty IsPageModeProperty =
        DependencyProperty.Register(nameof(IsPageMode), typeof(bool), typeof(ComposeBox), new PropertyMetadata(false, OnIsPageModeChanged));

    public bool IsPageMode
    {
        get => (bool)GetValue(IsPageModeProperty);
        set => SetValue(IsPageModeProperty, value);
    }

    private static void OnIsPageModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComposeBox { IsPageMode: true, IsLoaded: true } box)
            box.ViewModel.IsExpanded = true;
    }

    /// <summary>The user's own pin; the popup also stays open on its own while a draft is in the box.</summary>
    private bool _manuallyPinned;

    public ComposeBox()
    {
        InitializeComponent();

        // FocusRequested is taken from construction, not just from Loaded: a PostCard realizes
        // its inline box in response to ReplyTo changing, and BeginReply raises FocusRequested
        // right after - before the new box has loaded. PropertyChanged waits for Loaded, since
        // nothing it drives does anything until then.
        ViewModel.FocusRequested += OnFocusRequested;
        ViewModel.RestoreConfirmationRequested += OnRestoreConfirmationRequested;
        ViewModel.DraftRestored += OnDraftRestored;
        Loaded += ComposeBox_Loaded;

        // handledEventsToo: the editor handles drags itself (it accepts text, not files), so a plain
        // XAML DragOver/Drop handler on the border would never hear about a file dropped on the text area.
        DropZone.AddHandler(DragOverEvent, new DragEventHandler(DropZone_DragOver), handledEventsToo: true);
        DropZone.AddHandler(DropEvent, new DragEventHandler(DropZone_Drop), handledEventsToo: true);
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateLightDismissSuppression());

        // A PostCard's own inline box is created fresh with every thread page visit (PostPage
        // isn't cached); without this, each one leaks its subscription to the shared singleton.
        Unloaded += (_, _) =>
        {
            ViewModel.FocusRequested -= OnFocusRequested;
            ViewModel.RestoreConfirmationRequested -= OnRestoreConfirmationRequested;
            ViewModel.DraftRestored -= OnDraftRestored;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _lightDismissSuppression?.Dispose();
            _lightDismissSuppression = null;
            _emojiSuppressionTimer?.Stop();
            _emojiSuppression?.Dispose();
            _emojiSuppression = null;
        };
    }

    private void ComposeBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Unloaded dropped these; a recycled instance re-entering the tree needs them back to
        // keep its editor, focus and light-dismiss suppression in step with the draft. -= first
        // so the initial Loaded (FocusRequested already subscribed by the constructor) doesn't
        // double up.
        ViewModel.FocusRequested -= OnFocusRequested;
        ViewModel.FocusRequested += OnFocusRequested;
        ViewModel.RestoreConfirmationRequested -= OnRestoreConfirmationRequested;
        ViewModel.RestoreConfirmationRequested += OnRestoreConfirmationRequested;
        ViewModel.DraftRestored -= OnDraftRestored;
        ViewModel.DraftRestored += OnDraftRestored;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (IsPageMode)
            ViewModel.IsExpanded = true;
        UpdateLightDismissSuppression();
        UpdateMentionsButton();

        // Runs every time this instance (re)enters the visual tree, not just once: a PostCard's
        // inline box can be recycled by list virtualization, and the shared singleton VM's draft
        // may have changed to reflect a different post's reply while this instance was detached.
        if (!IsEditorReady)
        {
            // On a cold start (a traysky:compose link or a share launching the app) the editor's
            // TextDocument is created a beat after Loaded, and the draft arrives before that.
            SyncEditorWhenReady(attemptsLeft: 20);
            return;
        }

        if (RichSuggestTextPolicy.StripTokenMarkup(CurrentEditorText()) != ViewModel.Text)
            SetEditorText(ViewModel.Text);
    }

    private void SyncEditorWhenReady(int attemptsLeft)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!IsLoaded)
                return;

            if (!IsEditorReady)
            {
                if (attemptsLeft > 1)
                    SyncEditorWhenReady(attemptsLeft - 1);
                return;
            }

            if (RichSuggestTextPolicy.StripTokenMarkup(CurrentEditorText()) != ViewModel.Text)
                SetEditorText(ViewModel.Text);
        });
    }

    /// <summary>
    /// Composing often means copying text or a screenshot from another app, so a visible box
    /// holding a draft keeps the popup from light-dismissing when focus leaves it. An empty box
    /// (or one that's collapsed, e.g. a PostCard's unused inline reply) doesn't.
    /// </summary>
    private void UpdateLightDismissSuppression()
    {
        // The compose page's pin also holds the popup open, and its button reads as on whenever
        // the popup will stay open anyway (a draft can't be unpinned from under itself).
        bool hasDraft = PostTextPolicy.HasDraftContent(ViewModel.Text, ViewModel.Attachments.Count);
        bool visible = IsLoaded && Visibility == Visibility.Visible;
        bool shouldSuppress = visible && (hasDraft || (IsPageMode && _manuallyPinned));

        if (PinButton is not null)
        {
            PinButton.IsChecked = IsPageMode && (hasDraft || _manuallyPinned);
            ToolTipService.SetToolTip(PinButton, hasDraft
                ? "Staying open while there's a draft"
                : _manuallyPinned ? "Unpin" : "Keep this window open");
        }

        if (shouldSuppress)
        {
            _lightDismissSuppression ??= LightDismissSuppressionService.Suppress();
        }
        else
        {
            _lightDismissSuppression?.Dispose();
            _lightDismissSuppression = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComposeViewModel.Text) or nameof(ComposeViewModel.HasAttachments))
            UpdateLightDismissSuppression();

        if (e.PropertyName == nameof(ComposeViewModel.Text))
            UpdateMentionsButton();

        if (IsPageMode && e.PropertyName == nameof(ComposeViewModel.IsExpanded) && !ViewModel.IsExpanded)
            ViewModel.IsExpanded = true;

        if (e.PropertyName != nameof(ComposeViewModel.Text) || _syncingFromViewModel)
            return;

        // Each PostCard that has been replied to keeps its own ComposeBox, so several instances
        // of this control can share the singleton VM at once, most of them collapsed. A change from any one of them (including this one's own edit, echoed back
        // here) must not touch an instance whose TextDocument isn't ready yet - ComposeBox_Loaded
        // catches it up from the VM once it is.
        if (!IsEditorReady)
            return;

        // This instance's own edit already produced this value (Editor_TextChanged set it);
        // only a change from elsewhere - Post clearing the draft, or another ComposeBox instance
        // sharing the same singleton VM - needs to be pushed into this editor.
        if (RichSuggestTextPolicy.StripTokenMarkup(CurrentEditorText()) == ViewModel.Text)
            return;

        SetEditorText(ViewModel.Text);
    }

    /// <summary>RichEditBox doesn't create its TextDocument until it's loaded and templated; a collapsed/not-yet-loaded ComposeBox has neither.</summary>
    private bool IsEditorReady => IsLoaded && Editor.TextDocument is not null;

    private string CurrentEditorText()
    {
        Editor.TextDocument!.GetText(TextGetOptions.NoHidden, out string text);

        // RichEditBox separates paragraphs with a lone \r. Bluesky (and the web client) expect \n,
        // and a bare \r gets dropped or ignored there, which flattened multi-paragraph posts.
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private void SetEditorText(string text)
    {
        _syncingFromViewModel = true;
        try
        {
            Editor.TextDocument!.SetText(TextSetOptions.None, text);
            Editor.TextDocument.Selection.StartPosition = int.MaxValue;
            Editor.TextDocument.Selection.EndPosition = int.MaxValue;
        }
        finally
        {
            _syncingFromViewModel = false;
        }
    }

    private void Editor_TextChanged(RichSuggestBox sender, RoutedEventArgs e)
    {
        if (_syncingFromViewModel)
            return;

        ViewModel.Text = RichSuggestTextPolicy.StripTokenMarkup(CurrentEditorText());
        UpdateHashtagPopup();
    }

    /// <summary>Where the '#tag' being typed starts and ends in the editor's document, valid while the popup is open.</summary>
    private int _hashtagStart;
    private int _hashtagEnd;

    private const int HashtagLookback = HashtagFacetPolicy.MaxTagLength + 2;

    private void UpdateHashtagPopup()
    {
        if (!IsEditorReady || Editor.TextDocument!.Selection.Length != 0)
        {
            HashtagPopup.IsOpen = false;
            return;
        }

        int caret = Editor.TextDocument.Selection.StartPosition;
        int from = Math.Max(0, caret - HashtagLookback);
        Editor.TextDocument.GetRange(from, caret).GetText(TextGetOptions.None, out string before);

        if (!HashtagFacetPolicy.TryFindOpenTag(before, out int offset, out string query))
        {
            HashtagPopup.IsOpen = false;
            return;
        }

        IReadOnlyList<HashtagSuggestionItem> items = ViewModel.SearchHashtags(query);
        if (items.Count == 0)
        {
            HashtagPopup.IsOpen = false;
            return;
        }

        _hashtagStart = from + offset;
        _hashtagEnd = caret;
        HashtagList.ItemsSource = items;
        HashtagList.SelectedIndex = 0;
        HashtagPopupBorder.Width = Math.Clamp(Editor.ActualWidth, 160, 280);
        HashtagPopup.IsOpen = true;
        PlaceHashtagPopup(items.Count);
    }

    /// <summary>
    /// Puts the popup at the caret - below it, or above when the window has no room - instead of
    /// at the editor's bottom edge, which can be far from the text being typed (or off screen).
    /// </summary>
    private void PlaceHashtagPopup(int itemCount)
    {
        Editor.TextDocument!.Selection.GetRect(PointOptions.ClientCoordinates, out Windows.Foundation.Rect caret, out _);
        if (caret.IsEmpty || double.IsNaN(caret.X))
            return;

        double height = Math.Min(HashtagPopupBorder.MaxHeight, itemCount * 40 + 4);
        Windows.Foundation.Point below = Editor.TransformToVisual(HashtagPopup).TransformPoint(new Windows.Foundation.Point(caret.X, caret.Bottom + 2));
        Windows.Foundation.Point inRoot = Editor.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(caret.X, caret.Bottom + 2));

        bool fitsBelow = XamlRoot is not { } root || inRoot.Y + height <= root.Size.Height;
        double maxX = Math.Max(0, Editor.ActualWidth - HashtagPopupBorder.Width);
        HashtagPopup.HorizontalOffset = below.X - caret.X + Math.Clamp(caret.X, 0, maxX);
        HashtagPopup.VerticalOffset = fitsBelow ? below.Y : below.Y - caret.Height - 4 - height;
    }

    private void AcceptHashtag(HashtagSuggestionItem item)
    {
        HashtagPopup.IsOpen = false;

        string text = "#" + item.Tag + " ";
        Editor.TextDocument!.GetRange(_hashtagStart, _hashtagEnd).SetText(TextSetOptions.None, text);

        int end = _hashtagStart + text.Length;
        Editor.TextDocument.Selection.SetRange(end, end);
        ViewModel.RecordHashtagUsed(item.Tag);
    }

    private void HashtagList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HashtagSuggestionItem item)
            AcceptHashtag(item);
    }

    /// <summary>Arrows, Enter/Tab and Escape drive the '#' popup while it's open. Returns whether the key was consumed.</summary>
    private bool HandleHashtagKey(VirtualKey key)
    {
        if (!HashtagPopup.IsOpen)
            return false;

        int count = HashtagList.Items.Count;
        switch (key)
        {
            case VirtualKey.Down:
                HashtagList.SelectedIndex = Math.Min(count - 1, HashtagList.SelectedIndex + 1);
                return true;
            case VirtualKey.Up:
                HashtagList.SelectedIndex = Math.Max(0, HashtagList.SelectedIndex - 1);
                return true;
            case VirtualKey.Tab:
            case VirtualKey.Enter:
                if (HashtagList.SelectedItem is not HashtagSuggestionItem item)
                    return false;
                AcceptHashtag(item);
                return true;
            case VirtualKey.Escape:
                HashtagPopup.IsOpen = false;
                return true;
            default:
                return false;
        }
    }

    private async void Editor_SuggestionRequested(RichSuggestBox sender, SuggestionRequestedEventArgs args)
    {
        EventDeferral deferral = args.GetDeferral();
        try
        {
            string query = args.QueryText ?? string.Empty;

            if (args.Prefix == "@")
            {
                int epoch = ++_mentionRequestEpoch;
                IReadOnlyList<MentionSuggestionItem> results = await ViewModel.SearchMentionsAsync(query);
                if (epoch == _mentionRequestEpoch)
                    sender.ItemsSource = results;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Editor_SuggestionChosen(RichSuggestBox sender, SuggestionChosenEventArgs args)
    {
        if (args.Prefix == "@" && args.SelectedItem is MentionSuggestionItem mention)
        {
            args.DisplayText = mention.Handle;
        }
        else
        {
            return;
        }

        // RichSuggestBox's default hyperlink color is a fixed RGB baked straight into the
        // document's character formatting, not a ThemeResource, so it doesn't adapt to dark
        // mode on its own (reads as illegibly dark against a dark background). A ThemeResource
        // brush lookup from code doesn't reliably track the live theme here either, so this
        // branches on the editor's own ActualTheme and stamps in an explicit, checked-for-
        // contrast color per theme instead.
        if (args.Format is ITextCharacterFormat format)
            format.ForegroundColor = TokenColor();
    }

    private Windows.UI.Color TokenColor() => Editor.ActualTheme switch
    {
        ElementTheme.Dark => Windows.UI.Color.FromArgb(0xFF, 0x6C, 0xB4, 0xFF),
        _ => Windows.UI.Color.FromArgb(0xFF, 0x00, 0x5F, 0xB8)
    };

    private void OnFocusRequested(object? sender, System.EventArgs e) => FocusEditor();

    public void FocusEditor()
    {
        // The request can arrive before the control is in the tree (tray "Compose" action
        // while the popup is still opening); Loaded catches that case.
        if (IsLoaded)
        {
            Editor.Focus(FocusState.Programmatic);
            MoveCaretToEndIfReady();
            return;
        }

        void OnLoaded(object s, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            Editor.Focus(FocusState.Programmatic);
            MoveCaretToEndIfReady();
        }
        Loaded += OnLoaded;
    }

    /// <summary>
    /// RichEditBox can report Loaded before it's laid out enough to have created its native
    /// TextDocument (seen on a PostCard's inline reply box, which starts life collapsed with no
    /// size) - in that case there's no caret to move yet, so this is a no-op rather than a crash.
    /// </summary>
    private void MoveCaretToEndIfReady()
    {
        if (Editor.TextDocument is null)
            return;

        Editor.TextDocument.Selection.StartPosition = int.MaxValue;
        Editor.TextDocument.Selection.EndPosition = int.MaxValue;
    }

    /// <summary>Opens the reply / quote controls. A reply has no reply controls (they belong to the thread's root), so it only gets the quote switch.</summary>
    private async void Interaction_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not { } root)
            return;

        bool isReply = ViewModel.ReplyTo is not null;
        IReadOnlyList<ListItem> lists = await ViewModel.LoadReplyGateListsAsync();

        var answer = await Helpers.GateDialogs.AskAsync(root, isReply ? null : ViewModel.ReplySetting, ViewModel.AllowQuotes, lists);
        if (answer is { } chosen)
            ViewModel.ApplyInteractionChoice(chosen.Reply, chosen.AllowQuotes);
    }

    // The draft rows live in a flyout, outside the tree an ElementName binding to the root can reach, so they use click handlers.
    private void OpenDraft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DraftItem draft })
            ViewModel.OpenDraftCommand.Execute(draft);
    }

    private void DeleteDraft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DraftItem draft })
            ViewModel.DeleteDraftCommand.Execute(draft);
    }

    private void DraftsFlyout_Opening(object? sender, object e)
    {
        if (ViewModel.RefreshDraftsCommand.CanExecute(null))
            ViewModel.RefreshDraftsCommand.Execute(null);
    }

    /// <summary>Posting is public, so sending a scheduled thread early (or late) from the list asks first.</summary>
    private async void PostDraftNow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DraftItem draft } || XamlRoot is not { } root || !ViewModel.PostDraftNowCommand.CanExecute(draft))
            return;

        DraftsFlyout.Hide();
        if (await Helpers.GateDialogs.ConfirmPostNowAsync(root, draft.Preview))
            await ViewModel.PostDraftNowCommand.ExecuteAsync(draft);
    }

    // ----- Schedule -----

    /// <summary>Only the compose page asks about missed scheduled posts; a post's inline reply box has no room for it.</summary>
    public bool MissedBarOpen(bool pageMode, bool missed) => pageMode && missed;

    private void ReviewMissed_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsExpanded = true;
        DraftsFlyout.ShowAt(DraftsButton);
    }

    private void ScheduleFlyout_Opening(object? sender, object e)
    {
        DateTime suggested = ViewModel.SuggestedScheduleTime();
        ScheduleDate.MinDate = new DateTimeOffset(DateTime.Today);
        ScheduleDate.MaxDate = new DateTimeOffset(DateTime.Today + SchedulePolicy.MaxLead);
        ScheduleDate.Date = new DateTimeOffset(suggested.Date);
        ScheduleTime.Time = suggested.TimeOfDay;
        ScheduleError.Visibility = Visibility.Collapsed;
        ScheduleConfirmButton.IsEnabled = true;
    }

    private async void ScheduleConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleDate.Date is not { } date)
        {
            ShowScheduleError("Pick a date.");
            return;
        }

        // The picker's date part is the day chosen; the time is on this PC's clock.
        DateTimeOffset at = SchedulePolicy.FromLocal(date.Date, ScheduleTime.Time, TimeZoneInfo.Local);

        ScheduleConfirmButton.IsEnabled = false;
        try
        {
            string? problem = await ViewModel.ScheduleAsync(at);
            if (problem is null)
                ScheduleFlyout.Hide();
            else
                ShowScheduleError(problem);
        }
        finally
        {
            ScheduleConfirmButton.IsEnabled = true;
        }
    }

    private void ShowScheduleError(string message)
    {
        ScheduleError.Text = message;
        ScheduleError.Visibility = Visibility.Visible;
    }

    /// <summary>Several boxes share the view model; only the one whose Drafts flyout is open should ask.</summary>
    private async void OnRestoreConfirmationRequested(object? sender, DraftItem draft)
    {
        if (!DraftsFlyout.IsOpen || XamlRoot is not { } root)
            return;

        DraftsFlyout.Hide();
        if (await Helpers.GateDialogs.ConfirmReplaceDraftAsync(root))
            await ViewModel.RestoreDraftAsync(draft);
    }

    /// <summary>The flyout stays open when it has something to tell the user, such as media a draft couldn't bring back.</summary>
    private void OnDraftRestored(object? sender, System.EventArgs e)
    {
        if (!ViewModel.HasDraftsStatus)
            DraftsFlyout.Hide();
    }

    public Brush InteractionBrush(bool custom) =>
        (Brush)Application.Current.Resources[custom ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];

    public double EditorMinHeight(bool expanded, bool pageMode) => pageMode ? 220 : expanded ? 72 : 32;

    public double EditorMaxHeight(bool pageMode) => pageMode ? 420 : 160;

    // Action bar: the popup keeps everything on one row; full-page compose moves status/counter/Post to a second row.
    public int ActionRow(bool pageMode) => pageMode ? 1 : 0;

    public int StatusColumn(bool pageMode) => pageMode ? 0 : 10;

    public int StatusColumnSpan(bool pageMode) => pageMode ? 11 : 1;

    public double ActionRowSpacing(bool pageMode) => pageMode ? 6 : 0;

    /// <summary>Only offered while the draft mentions someone.</summary>
    private void UpdateMentionsButton() =>
        MentionsButton.Visibility = MentionScanPolicy.Handles(ViewModel.Text).Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void MentionsFlyout_Opening(object? sender, object e)
    {
        MentionsPanel.Children.Clear();
        foreach (string handle in MentionScanPolicy.Handles(ViewModel.Text))
        {
            var button = new Button
            {
                Content = "@" + handle,
                Tag = handle,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            button.Click += (s, _) =>
            {
                MentionsFlyout.Hide();
                if (s is FrameworkElement { Tag: string tag })
                    ProfilePreviewWindow.Show(tag);
            };
            MentionsPanel.Children.Add(button);
        }
    }

    // ----- Emoji picker -----

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    private const byte VkLWin = 0x5B;
    private const byte VkOemPeriod = 0xBE;
    private const uint KeyEventKeyUp = 0x0002;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _emojiSuppressionTimer;
    private IDisposable? _emojiSuppression;

    /// <summary>Opens Windows' own emoji panel (Win+.), which types into whatever has focus: the editor.</summary>
    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus(FocusState.Programmatic);

        // The panel is a separate window; without this its taking focus reads as the popup losing it and
        // light-dismisses the popup. Held for a while, then released, since there's no event for it closing.
        _emojiSuppression ??= LightDismissSuppressionService.Suppress();
        _emojiSuppressionTimer ??= DispatcherQueue.CreateTimer();
        _emojiSuppressionTimer.Interval = TimeSpan.FromSeconds(30);
        _emojiSuppressionTimer.IsRepeating = false;
        _emojiSuppressionTimer.Tick -= EmojiSuppressionTimer_Tick;
        _emojiSuppressionTimer.Tick += EmojiSuppressionTimer_Tick;
        _emojiSuppressionTimer.Start();

        keybd_event(VkLWin, 0, 0, 0);
        keybd_event(VkOemPeriod, 0, 0, 0);
        keybd_event(VkOemPeriod, 0, KeyEventKeyUp, 0);
        keybd_event(VkLWin, 0, KeyEventKeyUp, 0);
    }

    private void EmojiSuppressionTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        _emojiSuppression?.Dispose();
        _emojiSuppression = null;
    }

    // ----- Content warning -----

    /// <summary>Set while the radio list is being synced from the view model, so that doesn't read as a choice.</summary>
    private bool _syncingLabelChoice;

    private void LabelFlyout_Opening(object? sender, object e)
    {
        _syncingLabelChoice = true;
        try
        {
            if (LabelChoices.Items.Count == 0)
            {
                foreach (ComposeContentLabel label in ContentLabelPolicy.Choices)
                {
                    var row = new StackPanel();
                    row.Children.Add(new TextBlock { Text = ContentLabelPolicy.Title(label) });
                    row.Children.Add(new TextBlock
                    {
                        Text = ContentLabelPolicy.Description(label),
                        Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
                        TextWrapping = TextWrapping.Wrap
                    });
                    LabelChoices.Items.Add(row);
                }
            }

            LabelChoices.SelectedIndex = ContentLabelPolicy.Choices.ToList().IndexOf(ViewModel.ContentLabel);
        }
        finally
        {
            _syncingLabelChoice = false;
        }
    }

    private void LabelChoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLabelChoice || LabelChoices.SelectedIndex < 0)
            return;

        ViewModel.ContentLabel = ContentLabelPolicy.Choices[LabelChoices.SelectedIndex];
        LabelFlyout.Hide();
    }

    // ----- Thread posts: each follow-on has its own media and content warning -----

    /// <summary>Builds the menu for the thread post whose button opened it; the choice applies to that post only.</summary>
    private void ThreadLabelMenu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout { Target: FrameworkElement { Tag: ThreadPostItem item } } menu)
            return;

        menu.Items.Clear();
        foreach (ComposeContentLabel label in ContentLabelPolicy.Choices)
        {
            var entry = new RadioMenuFlyoutItem
            {
                Text = ContentLabelPolicy.Title(label),
                GroupName = "ThreadLabel" + item.Number,
                IsChecked = item.ContentLabel == label
            };
            ToolTipService.SetToolTip(entry, ContentLabelPolicy.Description(label));
            entry.Click += (_, _) => item.ContentLabel = label;
            menu.Items.Add(entry);
        }
    }

    /// <summary>A screenshot or file pasted into a thread post becomes that post's media, as it does for the first post.</summary>
    private async void ThreadPostText_Paste(object sender, TextControlPasteEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ThreadPostItem item })
            return;

        DataPackageView dataPackageView = Clipboard.GetContent();
        if (!dataPackageView.Contains(StandardDataFormats.Bitmap) && !dataPackageView.Contains(StandardDataFormats.StorageItems))
            return;

        e.Handled = true;
        await ViewModel.TryAttachFromClipboardAsync(dataPackageView, item);
    }

    // ----- Post language -----

    private void LanguageFlyout_Opening(object? sender, object e)
    {
        if (LanguagePanel.Children.Count == 0)
        {
            foreach (ComposeLanguagePolicy.Language language in ComposeLanguagePolicy.All)
            {
                var box = new CheckBox { Content = language.Name, Tag = language.Code, MinWidth = 0 };
                box.Click += LanguageBox_Click;
                LanguagePanel.Children.Add(box);
            }
        }

        SyncLanguageBoxes();
    }

    private void LanguageBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string code })
            ViewModel.ToggleLanguage(code);

        SyncLanguageBoxes();
    }

    /// <summary>Ticks what the view model holds and disables the rest once three are chosen.</summary>
    private void SyncLanguageBoxes()
    {
        foreach (CheckBox box in LanguagePanel.Children.OfType<CheckBox>())
        {
            string code = (string)box.Tag;
            bool selected = ViewModel.PostLanguages.Contains(code);
            box.IsChecked = selected;
            box.IsEnabled = ComposeLanguagePolicy.CanTick(ViewModel.PostLanguages, code);
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        // A draft already holds the popup open, so the click only records the user's intent for
        // when the draft is gone.
        _manuallyPinned = !_manuallyPinned;
        UpdateLightDismissSuppression();
    }

    public Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public string PostLabel(PostItem? replyTo, PostItem? quoteOf) => replyTo is not null ? "Reply" : quoteOf is not null ? "Quote" : "Post";

    public string StatusText(string? uploadStatus, string? videoLimits) => uploadStatus ?? videoLimits ?? "Ctrl+Enter to post";

    public Brush CounterBrush(bool near, bool over)
    {
        string key = over ? "SystemFillColorCriticalBrush" : near ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush";
        return (Brush)Application.Current.Resources[key];
    }

    private void Editor_GotFocus(object sender, RoutedEventArgs e)
    {
        ViewModel.IsExpanded = true;
    }

    private void Editor_LostFocus(object sender, RoutedEventArgs e)
    {
        HashtagPopup.IsOpen = false;

        // The compose page is all compose box; there is nothing to collapse into.
        if (IsPageMode)
            return;

        // Clicking another control in this same box (Attach image/video, for example) also
        // fires the editor's LostFocus; only collapse once focus has actually left the whole
        // box, not just the text field, or attaching a photo before typing anything would
        // shrink the box out from under the picker. Deferred one tick since focus hasn't
        // necessarily settled on the new element yet when LostFocus raises.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && IsWithin(focused, this))
                return;

            ViewModel.CollapseIfEmpty();
        });
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

        // Ctrl+Enter always posts, even with the '#' popup open.
        if (!(ctrl && e.Key == VirtualKey.Enter) && HandleHashtagKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Enter || !ctrl)
            return;

        e.Handled = true;
        if (ViewModel.PostCommand.CanExecute(null))
            ViewModel.PostCommand.Execute(null);
    }

    /// <summary>Text dragged onto the editor itself is the editor's to insert at the caret; everything else is ours.</summary>
    private bool LeaveToEditor(DragEventArgs e, ComposeDropKind kind) =>
        kind == ComposeDropKind.Text && e.OriginalSource is DependencyObject source && IsWithin(source, Editor);

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        ComposeDropKind kind = ComposeViewModel.ClassifyDrop(e.DataView);
        if (kind == ComposeDropKind.None || LeaveToEditor(e, kind))
            return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ComposeDropPolicy.Caption(kind);
        e.DragUIOverride.IsGlyphVisible = true;
        e.Handled = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        ComposeDropKind kind = ComposeViewModel.ClassifyDrop(e.DataView);
        if (kind == ComposeDropKind.None || LeaveToEditor(e, kind))
            return;

        e.Handled = true;

        // The drag data is only valid until the drop is completed, so hold it open across the awaits.
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            await ViewModel.AcceptDropAsync(e.DataView);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// A pasted screenshot or file must become a proper media attachment (uploaded as a Blob),
    /// not RichEditBox's own default paste behavior, which would either drop it as unusable
    /// inline content or silently discard it (ClipboardPasteFormat="PlainText" strips images).
    /// Handled is set before the first await, since the framework only honors it if it's true by
    /// the time this (async void) handler yields - setting it after the await would be too late.
    /// </summary>
    private async void Editor_Paste(RichSuggestBox sender, TextControlPasteEventArgs e)
    {
        DataPackageView dataPackageView = Clipboard.GetContent();
        if (!dataPackageView.Contains(StandardDataFormats.Bitmap) && !dataPackageView.Contains(StandardDataFormats.StorageItems))
            return;

        e.Handled = true;
        await ViewModel.TryAttachFromClipboardAsync(dataPackageView);
    }
}

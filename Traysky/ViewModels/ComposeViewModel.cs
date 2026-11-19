using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Embed;
using idunno.Bluesky.Feed;
using idunno.Bluesky.Video;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Win32;

namespace Traysky.ViewModels;

/// <summary>
/// The compose box: a new post, a reply, or a quote. One instance lives for the app's life so
/// a half-written post survives the flyout being dismissed; the text is also saved to settings
/// so it survives a restart.
/// </summary>
public sealed partial class ComposeViewModel : ObservableObject
{
    private static readonly Lazy<ComposeViewModel> _instance = new(() => new ComposeViewModel());

    public static ComposeViewModel Instance => _instance.Value;

    private ComposeViewModel()
    {
        Text = SettingsService.ComposeDraft;
        IsExpanded = Text.Length > 0;
        Drafts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowNoDrafts));
        ThreadPosts.CollectionChanged += OnThreadPostsChanged;
        Attachments.CollectionChanged += (_, _) =>
        {
            if (!HasVideoAttachment)
                VideoLimitsText = null;
            if (!HasAttachments)
                ContentLabel = ComposeContentLabel.None;
            if (UploadPhase == UploadPhase.Cancelled)
                SetPhase(UploadPhase.Idle); // the "Upload cancelled" note is stale once the attachments change
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(HasVideoAttachment));
            OnPropertyChanged(nameof(ImageAttachmentCount));
            OnPropertyChanged(nameof(CanAttachImage));
            OnPropertyChanged(nameof(CanAttachVideo));
            PostCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasLinkCard));
            OnPropertyChanged(nameof(HasLinkCardPlaceholder));
            PostCommand.NotifyCanExecuteChanged();
        };

        // Both singletons live for the app's life, so these never need undoing.
        ScheduledPostService scheduler = ScheduledPostService.Instance;
        scheduler.Changed += (_, _) => _ = ReloadLocalDraftsAsync();
        scheduler.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScheduledPostService.MissedCount))
            {
                OnPropertyChanged(nameof(HasMissedScheduled));
                OnPropertyChanged(nameof(MissedScheduledText));
            }
        };
    }

    // ----- Scheduling -----

    /// <summary>A reply goes out with its conversation, so only new posts and quotes can be scheduled.</summary>
    public bool CanSchedule => ReplyTo is null;

    /// <summary>Said after scheduling, once the box has emptied; gone when typing starts again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; private set; }

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>The text when <see cref="Notice"/> was set: it stays until the text really changes, not when the editor echoes it back.</summary>
    private string _textAtNotice = string.Empty;

    private static string NoticeKey(string text) => text.Replace("\r", string.Empty).Trim();

    private void SetNotice(string? message)
    {
        _textAtNotice = NoticeKey(Text);
        Notice = message;
    }

    public bool HasMissedScheduled => ScheduledPostService.Instance.MissedCount > 0;

    public string MissedScheduledText => SchedulePolicy.MissedBanner(ScheduledPostService.Instance.MissedCount);

    /// <summary>When the thread in the box was scheduled for before it was opened to edit, so the picker starts there.</summary>
    private DateTimeOffset? _rescheduleAt;

    /// <summary>Where the schedule picker starts, on the local wall clock.</summary>
    public DateTime SuggestedScheduleTime()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        return _rescheduleAt is { } previous && previous > now + SchedulePolicy.MinLead
            ? previous.ToLocalTime().DateTime
            : SchedulePolicy.DefaultLocalTime(now, TimeZoneInfo.Local);
    }

    /// <summary>
    /// Keeps the whole thread on this PC to be posted at <paramref name="at"/> (see
    /// <see cref="ScheduledPostService"/>) and empties the box. Returns null once it's scheduled, or why it can't be.
    /// </summary>
    public async Task<string?> ScheduleAsync(DateTimeOffset at)
    {
        if (ReplyTo is not null)
            return "Replies can't be scheduled. Post it now, or save it as a draft.";
        if (!BlueskySessionService.Instance.IsSignedIn)
            return "Sign in to schedule posts.";
        if (IsPosting)
            return "Wait for the post that's going out to finish.";
        if (SchedulePolicy.ValidateTime(at, DateTimeOffset.Now) is { } timeError)
            return timeError;
        if (Attachments.Any(a => a.IsOversized) || ThreadPosts.Any(t => t.HasOversized))
            return "An image is too large for Bluesky. Compress or remove it first.";
        if (PostTextPolicy.IsOverLimit(Text) || !ThreadPostPolicy.AllFit(ThreadPosts.Select(t => t.Text)))
            return $"A post is over the {PostTextPolicy.MaxGraphemes}-character limit.";
        if (!CanPost())
            return "Write something first.";

        return await SaveDraftCoreAsync(clearAfter: true, scheduleAt: at);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Counter))]
    [NotifyPropertyChangedFor(nameof(IsNearLimit))]
    [NotifyPropertyChangedFor(nameof(IsOverLimit))]
    [NotifyCanExecuteChangedFor(nameof(PostCommand))]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Collapsed to a single "What's up?" line until focused or given a context.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    [NotifyPropertyChangedFor(nameof(ContextLabel))]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    [NotifyPropertyChangedFor(nameof(InteractionSummary))]
    [NotifyPropertyChangedFor(nameof(HasCustomInteraction))]
    [NotifyPropertyChangedFor(nameof(CanSchedule))]
    public partial PostItem? ReplyTo { get; private set; }

    /// <summary>Who may reply to the post being written. Only applies to a post that starts a thread; a reply uses its thread's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InteractionSummary))]
    [NotifyPropertyChangedFor(nameof(HasCustomInteraction))]
    public partial ReplyChoice ReplySetting { get; set; } = ReplyChoice.Everyone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InteractionSummary))]
    [NotifyPropertyChangedFor(nameof(HasCustomInteraction))]
    public partial bool AllowQuotes { get; set; } = true;

    /// <summary>The languages this post is tagged with; remembered between posts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageLabel))]
    [NotifyPropertyChangedFor(nameof(LanguageDescription))]
    public partial IReadOnlyList<string> PostLanguages { get; private set; } =
        ComposeLanguagePolicy.Resolve(SettingsService.PostLanguages, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public string LanguageLabel => ComposeLanguagePolicy.Label(PostLanguages);

    public string LanguageDescription => $"Post language: {ComposeLanguagePolicy.Description(PostLanguages)}";

    /// <summary>Ticks or unticks a language, saving the choice. Refused ticks (a fourth language) leave it unchanged.</summary>
    public void ToggleLanguage(string code)
    {
        IReadOnlyList<string> updated = ComposeLanguagePolicy.Toggle(PostLanguages, code);
        if (ReferenceEquals(updated, PostLanguages))
            return;

        PostLanguages = updated;
        SettingsService.PostLanguages = ComposeLanguagePolicy.Serialize(updated);
    }

    /// <summary>A self-label ("content warning") for this post's media; dropped when the media is removed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContentLabel))]
    public partial ComposeContentLabel ContentLabel { get; set; }

    public bool HasContentLabel => ContentLabel != ComposeContentLabel.None;

    /// <summary>"Anyone can reply · Quotes allowed": shown on the compose box's settings button.</summary>
    public string InteractionSummary => InteractionGatePolicy.Summary(ReplySetting, AllowQuotes, ReplyTo is not null);

    /// <summary>Whether the controls differ from the defaults, so the button can stand out.</summary>
    /// <summary>The account's curation lists, for the "who can reply" dialog. A reply has no reply controls (they belong to the thread's root), so it offers none.</summary>
    public async Task<IReadOnlyList<ListItem>> LoadReplyGateListsAsync()
    {
        if (ReplyTo is not null)
            return [];

        return (PreviewMode.IsEnabled ? PreviewMode.SampleLists() : await ListsService.GetOwnCurationListsAsync()) ?? [];
    }

    /// <summary>Stores what the reply / quote dialog chose; a null <paramref name="reply"/> leaves the reply setting alone.</summary>
    public void ApplyInteractionChoice(ReplyChoice? reply, bool allowQuotes)
    {
        if (reply is not null)
            ReplySetting = reply;
        AllowQuotes = allowQuotes;
    }

    public bool HasCustomInteraction =>!InteractionGatePolicy.IsDefault(ReplyTo is null ? ReplySetting : ReplyChoice.Everyone, AllowQuotes);

    /// <summary>The draft the box was restored from, so saving again updates it and posting removes it.</summary>
    private TimestampIdentifier? _draftId;

    public ObservableCollection<DraftItem> Drafts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoDrafts))]
    public partial bool IsLoadingDrafts { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDraftsStatus))]
    public partial string? DraftsStatus { get; private set; }

    public bool HasDraftsStatus => !string.IsNullOrEmpty(DraftsStatus);

    public bool ShowNoDrafts => !IsLoadingDrafts && Drafts.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    [NotifyPropertyChangedFor(nameof(ContextLabel))]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    [NotifyPropertyChangedFor(nameof(HasLinkCard))]
    [NotifyPropertyChangedFor(nameof(HasLinkCardPlaceholder))]
    public partial PostItem? QuoteOf { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PostCommand))]
    public partial bool IsPosting { get; private set; }

    [ObservableProperty]
    public partial string? UploadStatus { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelUpload))]
    [NotifyCanExecuteChangedFor(nameof(CancelUploadCommand))]
    public partial UploadPhase UploadPhase { get; private set; }

    public bool CanCancelUpload => UploadProgressPolicy.CanCancel(UploadPhase);

    /// <summary>0–1 while a multipart video upload reports progress; null when indeterminate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUploadProgress))]
    [NotifyPropertyChangedFor(nameof(UploadProgressValue))]
    public partial double? UploadProgress { get; private set; }

    public bool HasUploadProgress => UploadProgress is not null;

    public double UploadProgressValue => UploadProgress ?? 0;

    /// <summary>"3 videos, 120 MB left today", from the last <c>getUploadLimits</c> check; null when unlimited or unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideoLimitsText))]
    public partial string? VideoLimitsText { get; private set; }

    public bool HasVideoLimitsText => !string.IsNullOrEmpty(VideoLimitsText);

    // The compose box is a singleton, so closing the popup or navigating away does not cancel an
    // in-flight upload: it finishes (or fails) in the background and the post goes out. Drafts only
    // store text and picked files, never upload state, so a draft can't capture a half-finished upload.
    private CancellationTokenSource? _uploadCts;

    private void SetPhase(UploadPhase phase, bool isVideo = false)
    {
        UploadPhase = phase;
        UploadStatus = UploadProgressPolicy.StatusText(phase, isVideo, Attachments.Count);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    /// <summary>Images or a video picked for this post, not yet uploaded. Images and video are mutually exclusive.</summary>
    public ObservableCollection<ComposeAttachmentItem> Attachments { get; } = [];

    /// <summary>Raised on the UI thread after a post goes through, so the timeline can refresh.</summary>
    public event EventHandler? Posted;

    /// <summary>Bumped just before <see cref="Posted"/>, so a page can tell whether a post went through while it wasn't showing.</summary>
    public int PostedCount { get; private set; }

    /// <summary>Raised when something wants the text box focused (tray "Compose" action, reply button).</summary>
    public event EventHandler? FocusRequested;

    public string Counter => $"{PostTextPolicy.CountGraphemes(Text)}/{PostTextPolicy.MaxGraphemes}";

    public bool IsNearLimit => PostTextPolicy.IsNearLimit(Text) && !PostTextPolicy.IsOverLimit(Text);

    public bool IsOverLimit => PostTextPolicy.IsOverLimit(Text);

    public bool HasContext => ReplyTo is not null || QuoteOf is not null;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasAttachments => Attachments.Count > 0;

    public bool HasVideoAttachment => Attachments.Any(a => a.IsVideo);

    public int ImageAttachmentCount => Attachments.Count(a => !a.IsVideo);

    public bool CanAttachImage => ComposeAttachmentPolicy.CanAddImage(ImageAttachmentCount, HasVideoAttachment);

    public bool CanAttachVideo => ComposeAttachmentPolicy.CanAddVideo(ImageAttachmentCount, HasVideoAttachment);

    public string ContextLabel => ReplyTo is not null
        ? $"Replying to @{ReplyTo.AuthorHandle}"
        : QuoteOf is not null ? $"Quoting @{QuoteOf.AuthorHandle}" : string.Empty;

    public string Placeholder => ReplyTo is not null ? "Write your reply" : QuoteOf is not null ? "Add a comment" : "What's up?";

    public void BeginReply(PostItem post)
    {
        QuoteOf = null;
        ReplyTo = post;
        IsExpanded = true;
        Error = null;
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void BeginQuote(PostItem post)
    {
        ReplyTo = null;
        QuoteOf = post;
        IsExpanded = true;
        Error = null;
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void BeginNew()
    {
        IsExpanded = true;
        Error = null;
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearContext()
    {
        ReplyTo = null;
        QuoteOf = null;
    }

    [RelayCommand]
    private void RemoveAttachment(ComposeAttachmentItem attachment)
    {
        if (UploadProgressPolicy.CanEditAttachments(UploadPhase))
            OwnerOf(attachment)?.Remove(attachment);
    }

    /// <summary>The attachment list (the first post's, or a thread post's) that holds <paramref name="attachment"/>, so shared remove / compress commands work in any post.</summary>
    private ObservableCollection<ComposeAttachmentItem>? OwnerOf(ComposeAttachmentItem attachment) =>
        Attachments.Contains(attachment)
            ? Attachments
            : ThreadPosts.Select(t => t.Attachments).FirstOrDefault(c => c.Contains(attachment));

    /// <summary>The attachments of the post being edited: a thread post's, or the first post's when <paramref name="target"/> is null.</summary>
    private ObservableCollection<ComposeAttachmentItem> MediaOf(ThreadPostItem? target) => target?.Attachments ?? Attachments;

    private bool CanImage(ThreadPostItem? target) => target?.CanAttachImage ?? CanAttachImage;

    private bool CanVideo(ThreadPostItem? target) => target?.CanAttachVideo ?? CanAttachVideo;

    [RelayCommand(CanExecute = nameof(CanCancelUpload))]
    private void CancelUpload() => _uploadCts?.Cancel();

    [RelayCommand]
    private Task AttachImageAsync() => AttachAsync(ComposeAttachmentKind.Image);

    [RelayCommand]
    private Task AttachVideoAsync() => AttachAsync(ComposeAttachmentKind.Video);

    [RelayCommand]
    private Task AttachThreadImageAsync(ThreadPostItem item) => AttachAsync(ComposeAttachmentKind.Image, item);

    [RelayCommand]
    private Task AttachThreadVideoAsync(ThreadPostItem item) => AttachAsync(ComposeAttachmentKind.Video, item);

    private async Task AttachAsync(ComposeAttachmentKind kind, ThreadPostItem? target = null)
    {
        if (kind == ComposeAttachmentKind.Image && !CanImage(target))
        {
            Error = $"Up to {ComposeAttachmentPolicy.MaxImages} images per post, or one video.";
            return;
        }

        if (kind == ComposeAttachmentKind.Video && !CanVideo(target))
        {
            Error = "A post can have images or one video, not both.";
            return;
        }

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = kind == ComposeAttachmentKind.Video ? PickerLocationId.VideosLibrary : PickerLocationId.PicturesLibrary
        };
        foreach (string extension in kind == ComposeAttachmentKind.Image ? ComposeAttachmentPolicy.ImageExtensions : ComposeAttachmentPolicy.VideoExtensions)
            picker.FileTypeFilter.Add(extension);

        // The popup is the foreground window whenever this can be invoked, so there's no need
        // to thread a Window reference all the way down from App into a deeply-nested control.
        nint hwnd = PInvoke.GetForegroundWindow();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        Error = null;

        // The picker's own window takes the OS foreground while it's open, which isn't a WinUI
        // popup HWND the tray flyout already knows to ignore — without this it reads as focus
        // having left the flyout entirely and light-dismisses it mid-pick.
        using IDisposable suppression = LightDismissSuppressionService.Suppress();

        if (kind == ComposeAttachmentKind.Video)
        {
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
                await AddVideoAsync(file, target);
            return;
        }

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        foreach (StorageFile file in files)
        {
            if (!CanImage(target))
            {
                Error = $"Up to {ComposeAttachmentPolicy.MaxImages} images per post.";
                break;
            }

            await AddImageAsync(file, target);
        }
    }

    /// <summary>
    /// Routes clipboard content (Ctrl+V in the editor) into the same attachment pipeline as the
    /// file picker instead of the editor's own paste - a screenshot or a file copied from
    /// Explorer needs to become a Blob upload, not text/inline content dropped into the post
    /// body. Handles a raw bitmap (screenshot, image copied from a browser) via
    /// <see cref="DataPackageView.GetBitmapAsync"/> and one or more files copied from Explorer
    /// via <see cref="StandardDataFormats.StorageItems"/>, applying the same image/video
    /// exclusivity and count rules as picking files does.
    /// </summary>
    public async Task TryAttachFromClipboardAsync(DataPackageView dataPackageView, ThreadPostItem? target = null)
    {
        Error = null;

        if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await dataPackageView.GetStorageItemsAsync();
            bool attachedAny = await AddFilesAsync(items.OfType<StorageFile>(), target);

            if (!attachedAny && Error is null && items.Count > 0)
                Error = "That doesn't contain a supported image or video.";

            return;
        }

        if (dataPackageView.Contains(StandardDataFormats.Bitmap))
        {
            if (!CanImage(target))
            {
                Error = $"Up to {ComposeAttachmentPolicy.MaxImages} images per post, or one video.";
                return;
            }

            RandomAccessStreamReference streamRef = await dataPackageView.GetBitmapAsync();
            await AddClipboardBitmapAsync(streamRef, target);
        }
    }

    /// <summary>
    /// Takes in something dragged onto the compose box. Files and pictures go through the same
    /// attach rules as paste; plain text (or a dragged link) is appended to the draft.
    /// </summary>
    public async Task AcceptDropAsync(DataPackageView dataPackageView)
    {
        ComposeDropKind kind = ClassifyDrop(dataPackageView);
        if (kind == ComposeDropKind.None)
            return;

        IsExpanded = true;

        if (kind == ComposeDropKind.Media)
        {
            await TryAttachFromClipboardAsync(dataPackageView);
            return;
        }

        string? dropped = dataPackageView.Contains(StandardDataFormats.Text)
            ? await dataPackageView.GetTextAsync()
            : (await dataPackageView.GetWebLinkAsync())?.AbsoluteUri;

        if (string.IsNullOrWhiteSpace(dropped))
            return;

        Error = null;
        Text = ShareTargetPolicy.MergeIntoDraft(Text, dropped.Trim());
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public static ComposeDropKind ClassifyDrop(DataPackageView dataPackageView) => ComposeDropPolicy.Classify(
        dataPackageView.Contains(StandardDataFormats.StorageItems),
        dataPackageView.Contains(StandardDataFormats.Bitmap),
        dataPackageView.Contains(StandardDataFormats.Text),
        dataPackageView.Contains(StandardDataFormats.WebLink));

    /// <summary>
    /// Takes in something shared to Traysky from another app (see <see cref="ShareTargetService"/>)
    /// as a fresh post: any reply/quote in progress is dropped, the shared text goes below
    /// whatever draft was already typed, and the files go through the same rules as picking them.
    /// </summary>
    public async Task AcceptShareAsync(StagedShare share)
    {
        ReplyTo = null;
        QuoteOf = null;
        Text = ShareTargetPolicy.MergeIntoDraft(Text, share.Text);
        Error = null;
        IsExpanded = true;

        var files = new List<StorageFile>(share.FilePaths.Count);
        foreach (string path in share.FilePaths)
            files.Add(await StorageFile.GetFileFromPathAsync(path));

        await AddFilesAsync(files);
    }

    /// <summary>
    /// Attaches files by extension, applying the image/video exclusivity and count rules; a file
    /// that doesn't fit leaves <see cref="Error"/> set. Returns whether anything was attached.
    /// </summary>
    private async Task<bool> AddFilesAsync(IEnumerable<StorageFile> files, ThreadPostItem? target = null)
    {
        bool attachedAny = false;

        foreach (StorageFile file in files)
        {
            string extension = Path.GetExtension(file.Name);
            if (ComposeAttachmentPolicy.IsVideoExtension(extension))
            {
                if (!CanVideo(target))
                {
                    Error = "A post can have images or one video, not both.";
                    continue;
                }

                await AddVideoAsync(file, target);
                attachedAny = true;
            }
            else if (ComposeAttachmentPolicy.IsImageExtension(extension))
            {
                if (!CanImage(target))
                {
                    Error = $"Up to {ComposeAttachmentPolicy.MaxImages} images per post, or one video.";
                    continue;
                }

                await AddImageAsync(file, target);
                attachedAny = true;
            }
        }

        return attachedAny;
    }

    /// <summary>
    /// A clipboard bitmap has no filename or guaranteed encoding, so it's decoded and re-encoded
    /// as PNG rather than trusted as-is, to match what <see cref="ComposeAttachmentPolicy"/> and
    /// Bluesky's blob upload expect.
    /// </summary>
    private async Task AddClipboardBitmapAsync(RandomAccessStreamReference streamRef, ThreadPostItem? target = null)
    {
        using IRandomAccessStreamWithContentType sourceStream = await streamRef.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(sourceStream);
        SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        using var encodedStream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, encodedStream);
        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync();

        var bytes = new byte[encodedStream.Size];
        encodedStream.Seek(0);
        using (var reader = new DataReader(encodedStream))
        {
            await reader.LoadAsync((uint)encodedStream.Size);
            reader.ReadBytes(bytes);
            // Leaves encodedStream open - it's reused below for the thumbnail - the same
            // precaution ToInMemoryStreamAsync takes with DataWriter.DetachStream().
            reader.DetachStream();
        }

        await AddImageBytesAsync(bytes, "image/png", $"pasted-{DateTime.Now:yyyyMMddHHmmss}.png", target);
    }

    private async Task AddImageAsync(StorageFile file, ThreadPostItem? target = null)
    {
        string extension = Path.GetExtension(file.Name);
        string? mimeType = ComposeAttachmentPolicy.MimeTypeFor(extension);
        if (mimeType is null || !ComposeAttachmentPolicy.IsImageExtension(extension))
        {
            Error = "Unsupported image type.";
            return;
        }

        await AddImageBytesAsync(await ReadAllBytesAsync(file), mimeType, file.Name, target);
    }

    /// <summary>
    /// Adds an image, shrinking it first if it's over the blob limit: a gentle pass that keeps every
    /// pixel runs automatically; if that isn't enough the image is added flagged as oversized, with a
    /// Compress button that applies the lossy ladder. Animated GIFs can't be re-encoded, so they are rejected.
    /// </summary>
    private async Task AddImageBytesAsync(byte[] bytes, string mimeType, string fileName, ThreadPostItem? target = null)
    {
        bool oversized = ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Image, bytes.Length) is not null;
        int? width = null, height = null;

        if (oversized)
        {
            if (!ImageCompressionPolicy.CanCompress(mimeType))
            {
                Error = ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Image, bytes.Length);
                return;
            }

            CompressedImage? gentle = await TryCompressAsync(bytes, ImageCompressionPolicy.GentleSteps);
            if (gentle is not null)
            {
                bytes = gentle.Bytes;
                mimeType = "image/jpeg";
                fileName = Path.ChangeExtension(fileName, ".jpg");
                width = gentle.Width;
                height = gentle.Height;
                oversized = false;
            }
        }

        using InMemoryRandomAccessStream memoryStream = await ToInMemoryStreamAsync(bytes);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(memoryStream);

        MediaOf(target).Add(new ComposeAttachmentItem
        {
            Bytes = bytes,
            MimeType = mimeType,
            FileName = fileName,
            Kind = ComposeAttachmentKind.Image,
            PixelWidth = width ?? (int)decoder.PixelWidth,
            PixelHeight = height ?? (int)decoder.PixelHeight,
            IsOversized = oversized
        });
    }

    private static async Task<CompressedImage?> TryCompressAsync(byte[] bytes, IReadOnlyList<ImageCompressionPolicy.Step> steps)
    {
        try
        {
            return await ImageCompressor.TryCompressAsync(bytes, steps, ComposeAttachmentPolicy.MaxImageBytes);
        }
        catch (Exception ex)
        {
            LogService.Warn("Compose", $"Image compression threw: {ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    private async Task CompressAttachmentAsync(ComposeAttachmentItem attachment)
    {
        ObservableCollection<ComposeAttachmentItem>? owner = OwnerOf(attachment);
        int index = owner?.IndexOf(attachment) ?? -1;
        if (owner is null || index < 0)
            return;

        Error = null;
        CompressedImage? result = await TryCompressAsync(attachment.Bytes, ImageCompressionPolicy.LossySteps);
        if (result is null)
        {
            Error = "Couldn't shrink this image enough. Try a smaller one.";
            return;
        }

        index = owner.IndexOf(attachment); // the list may have changed while compressing
        if (index < 0)
            return;

        owner[index] = new ComposeAttachmentItem
        {
            Bytes = result.Bytes,
            MimeType = "image/jpeg",
            FileName = Path.ChangeExtension(attachment.FileName, ".jpg"),
            Kind = ComposeAttachmentKind.Image,
            PixelWidth = result.Width,
            PixelHeight = result.Height,
            AltText = attachment.AltText
        };
    }

    private async Task AddVideoAsync(StorageFile file, ThreadPostItem? target = null)
    {
        string extension = Path.GetExtension(file.Name);
        string? mimeType = ComposeAttachmentPolicy.MimeTypeFor(extension);
        if (mimeType is null || !ComposeAttachmentPolicy.IsVideoExtension(extension))
        {
            Error = "Unsupported video type.";
            return;
        }

        byte[] bytes = await ReadAllBytesAsync(file);
        string? sizeError = ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Video, bytes.Length);
        if (sizeError is not null)
        {
            Error = sizeError;
            return;
        }

        // Preflight against today's video allowance so it fails here rather than after a long upload.
        // If the check itself fails the attach still goes ahead; the video service is authoritative.
        if (BlueskySessionService.Instance.IsSignedIn)
        {
            var limits = await VideoUploadService.GetLimitsAsync(BlueskySessionService.Instance.Agent);
            if (limits is { Succeeded: true, Result: { } allowance })
            {
                string? limitError = ComposeAttachmentPolicy.ValidateVideoLimits(
                    allowance.CanUpload, allowance.RemainingDailyVideos, allowance.RemainingDailyBytes, bytes.Length);
                VideoLimitsText = ComposeAttachmentPolicy.LimitsSummary(allowance.RemainingDailyVideos, allowance.RemainingDailyBytes);
                if (limitError is not null)
                {
                    Error = limitError;
                    return;
                }
            }
        }

        MediaOf(target).Add(new ComposeAttachmentItem
        {
            Bytes = bytes,
            MimeType = mimeType,
            FileName = file.Name,
            Kind = ComposeAttachmentKind.Video
        });
    }

    internal static async Task<byte[]> ReadAllBytesAsync(StorageFile file)
    {
        using IRandomAccessStream stream = await file.OpenReadAsync();
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    internal static async Task<InMemoryRandomAccessStream> ToInMemoryStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        return stream;
    }

    /// <summary>Collapse back to one line if nothing is typed, no reply/quote is pending, and nothing is attached.</summary>
    public void CollapseIfEmpty()
    {
        if (string.IsNullOrWhiteSpace(Text) && !HasContext && !HasAttachments && !HasThreadPosts)
            IsExpanded = false;
    }

    /// <summary>
    /// Live results for the RichSuggestBox '@' popup. Debounced slightly since it fires on every
    /// keystroke; <see cref="Traysky.Controls.ComposeBox"/> also discards a stale response that
    /// lands after a newer request, so a slow reply here just never gets shown.
    /// </summary>
    public async Task<IReadOnlyList<MentionSuggestionItem>> SearchMentionsAsync(string query)
    {
        string trimmed = query.Trim();
        if (trimmed.Length == 0)
            return [];

        try
        {
            await Task.Delay(150);

            AtProtoHttpResult<PagedViewReadOnlyCollection<ProfileViewBasic>> result =
                await BlueskySessionService.Instance.Agent.SearchActorsTypeahead(trimmed, limit: 8);

            if (!result.Succeeded || result.Result is null)
                return [];

            return result.Result.Select(FeedMapper.ToMentionSuggestion).ToList();
        }
        catch (Exception ex)
        {
            LogService.Warn("Compose", $"Mention search for '{trimmed}' failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Results for the RichSuggestBox '#' popup. Bluesky has no tag-search API, so this is just
    /// the user's own recently-used tags (<see cref="SettingsService.RecentHashtags"/>) filtered
    /// by what they've typed, plus the typed text itself so it can always be picked as a token.
    /// </summary>
    public IReadOnlyList<HashtagSuggestionItem> SearchHashtags(string query)
    {
        string normalized = HashtagSuggestionPolicy.Normalize(query);
        IReadOnlyList<string> matches = HashtagSuggestionPolicy.Filter(SettingsService.RecentHashtags, normalized);

        var results = new List<HashtagSuggestionItem>(matches.Count + 1);
        if (normalized.Length > 0 && !matches.Any(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)))
            results.Add(new HashtagSuggestionItem { Tag = normalized, IsRecent = false });

        results.AddRange(matches.Select(t => new HashtagSuggestionItem { Tag = t, IsRecent = true }));
        return results;
    }

    /// <summary>Remembers a chosen '#' tag so it resurfaces next time the user starts typing one like it.</summary>
    public void RecordHashtagUsed(string tag) =>
        SettingsService.RecentHashtags = HashtagSuggestionPolicy.WithRecentTag(SettingsService.RecentHashtags, tag);

    partial void OnTextChanged(string value)
    {
        SettingsService.ComposeDraft = value;
        if (Error is not null)
            Error = null;
        if (Notice is not null && NoticeKey(value) != _textAtNotice)
            Notice = null;

        _ = UpdateLinkCardAsync(value);
    }

    private EmbeddedExternal? _linkCard;
    private CancellationTokenSource? _linkCardCts;
    private Uri? _linkCardUri;
    private Uri? _dismissedLinkUri;

    /// <summary>The card to preview and attach, hidden whenever images, video or a quote already use the post's one embed slot.</summary>
    public bool HasLinkCard => _linkCard is not null && LinkCardPolicy.CanAttachCard(HasAttachments, QuoteOf is not null);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkCardPlaceholder))]
    public partial bool IsLoadingLinkCard { get; private set; }

    /// <summary>Shown while the card is being fetched, so the box doesn't jump in size only when it lands.</summary>
    public bool HasLinkCardPlaceholder => IsLoadingLinkCard && LinkCardPolicy.CanAttachCard(HasAttachments, QuoteOf is not null);

    public string LinkCardHost => _linkCardUri?.Host ?? string.Empty;

    public string LinkCardTitle => _linkCard?.External.Title ?? string.Empty;

    public string LinkCardDescription => _linkCard?.External.Description ?? string.Empty;

    public bool HasLinkCardDescription => !string.IsNullOrEmpty(LinkCardDescription);

    public Uri? LinkCardThumbnail { get; private set; }

    public bool HasLinkCardThumbnail => LinkCardThumbnail is not null;

    private void NotifyLinkCardChanged()
    {
        OnPropertyChanged(nameof(HasLinkCard));
        OnPropertyChanged(nameof(HasLinkCardPlaceholder));
        OnPropertyChanged(nameof(LinkCardHost));
        OnPropertyChanged(nameof(LinkCardTitle));
        OnPropertyChanged(nameof(LinkCardDescription));
        OnPropertyChanged(nameof(HasLinkCardDescription));
        OnPropertyChanged(nameof(LinkCardThumbnail));
        OnPropertyChanged(nameof(HasLinkCardThumbnail));
    }

    private void ClearLinkCard()
    {
        _linkCardCts?.Cancel();
        _linkCardCts = null;
        _linkCardUri = null;
        _linkCard = null;
        LinkCardThumbnail = null;
        IsLoadingLinkCard = false;
        NotifyLinkCardChanged();
    }

    [RelayCommand]
    private void DismissLinkCard()
    {
        _dismissedLinkUri = _linkCardUri;
        ClearLinkCard();
    }

    /// <summary>
    /// Keeps the link card in step with the draft: after typing pauses, the first link in the
    /// text is looked up (Open Graph title/description/image, as bsky.app does) and previewed.
    /// A link the user dismissed the card for stays dismissed until it leaves the text.
    /// </summary>
    private async Task UpdateLinkCardAsync(string text)
    {
        Uri? uri = LinkCardPolicy.FirstLink(text);

        if (uri is null)
        {
            _dismissedLinkUri = null;
            if (_linkCardUri is not null || _linkCardCts is not null)
                ClearLinkCard();
            return;
        }

        if (uri == _linkCardUri || uri == _dismissedLinkUri)
            return;

        // A different link than the one on screen or in flight: drop it and start over.
        ClearLinkCard();
        _dismissedLinkUri = null;

        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        var cts = new CancellationTokenSource();
        _linkCardCts = cts;
        _linkCardUri = uri;
        IsLoadingLinkCard = true;
        OnPropertyChanged(nameof(LinkCardHost));

        try
        {
            await Task.Delay(LinkCardPolicy.Debounce, cts.Token);

            BlueskyAgent agent = BlueskySessionService.Instance.Agent;
            EmbeddedExternal? card = await agent.CreateOpenGraphEmbeddedCardGenerator().Generate(uri, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            if (card is null)
                return;

            _linkCard = card;
            LinkCardThumbnail = card.External.Thumbnail is { } blob
                ? LinkCardPolicy.ThumbnailPreviewUri(agent.Did?.ToString(), blob.Reference.Link.ToString())
                : null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogService.Warn("Compose", $"Link preview for '{uri.Host}' failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_linkCardCts, cts))
            {
                IsLoadingLinkCard = false;
                NotifyLinkCardChanged();
            }
        }
    }

    [RelayCommand]
    private async Task RefreshDraftsAsync()
    {
        if (IsLoadingDrafts)
            return;

        IsLoadingDrafts = true;
        DraftsStatus = null;
        try
        {
            List<DraftItem>? remote = PreviewMode.IsEnabled ? PreviewMode.SampleDrafts() : await DraftsService.GetAllAsync();
            List<SavedThread> local = PreviewMode.IsEnabled ? [] : await SavedThreadStore.ListAsync();

            if (remote is null && local.Count == 0)
            {
                DraftsStatus = BlueskySessionService.Instance.IsSignedIn ? "Couldn't load your drafts. Try again in a moment." : "Sign in to use drafts.";
                return;
            }

            ShowDrafts(local, remote ?? []);

            if (remote is null)
                DraftsStatus = "Couldn't load your Bluesky drafts, so only the ones saved on this PC are shown.";
        }
        finally
        {
            IsLoadingDrafts = false;
        }
    }

    /// <summary>
    /// Fills the list: threads waiting for a decision first, then what's scheduled soonest first, then
    /// drafts newest first. A thread kept on this PC that was mirrored to Bluesky shows once (as the
    /// local copy, which has the media).
    /// </summary>
    private void ShowDrafts(List<SavedThread> local, IEnumerable<DraftItem> remote)
    {
        HashSet<string> mirrored = local.Where(t => t.BlueskyDraftId is not null).Select(t => t.BlueskyDraftId!).ToHashSet(StringComparer.Ordinal);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<DraftItem> merged = local.Select(t => ToLocalItem(t, now))
            .Concat(remote.Where(d => d.Id is null || !mirrored.Contains(d.Id.ToString()!)))
            .OrderBy(d => d.SortGroup)
            .ThenBy(d => d.SortGroup < 2 ? d.ScheduledAt : null)
            .ThenByDescending(d => d.SortKey)
            .ToList();

        Drafts.Clear();
        foreach (DraftItem draft in merged)
            Drafts.Add(draft);
    }

    /// <summary>
    /// The scheduler posted, failed or cancelled a thread on this PC: redraws the list from disk,
    /// keeping the Bluesky drafts already shown, so nothing goes over the network while hidden.
    /// </summary>
    private async Task ReloadLocalDraftsAsync()
    {
        if (IsLoadingDrafts || Drafts.Count == 0 || PreviewMode.IsEnabled)
            return;

        List<DraftItem> remote = Drafts.Where(d => d.Local is null).ToList();
        List<SavedThread> local = await SavedThreadStore.ListAsync();
        if (!IsLoadingDrafts)
            ShowDrafts(local, remote);
    }

    /// <summary>
    /// Sends a scheduled thread now, from its row in the list: early, after it was missed, or to finish
    /// one that was cut off. The list has closed by then, so the outcome is said in the box.
    /// </summary>
    [RelayCommand]
    private async Task PostDraftNowAsync(DraftItem draft)
    {
        if (draft.LocalId is not { } id || PreviewMode.IsEnabled)
            return;

        Error = null;
        SetNotice("Posting your scheduled post…");
        string? problem = await ScheduledPostService.Instance.PostNowAsync(id);

        if (problem is null)
        {
            SetNotice("Posted.");

            // The timeline picks this up when it's next shown, as it does for a post from the box.
            PostedCount++;
        }
        else if (problem == SchedulePolicy.CancelledNote)
        {
            SetNotice(problem);
        }
        else
        {
            SetNotice(null);
            Error = $"Couldn't post it. {problem}";
        }
    }

    /// <summary>Saves what's in the box as a draft (updating the one it was restored from) and leaves the box as it is.</summary>
    [RelayCommand]
    private Task SaveDraftAsync() => SaveDraftCoreAsync(clearAfter: false);

    /// <summary>Saves the whole thread and empties the box, for setting a post aside.</summary>
    [RelayCommand]
    private Task SaveDraftAndClearAsync() => SaveDraftCoreAsync(clearAfter: true);

    /// <summary>The id of the thread on this PC the box was restored from or last saved as, so saving again updates it.</summary>
    private string? _savedThreadId;

    private static SavedMedia ToSavedMedia(ComposeAttachmentItem a) => new()
    {
        FileName = a.FileName,
        MimeType = a.MimeType,
        IsVideo = a.IsVideo,
        AltText = a.AltText,
        PixelWidth = a.PixelWidth,
        PixelHeight = a.PixelHeight,
        Bytes = a.Bytes
    };

    private static ComposeAttachmentItem ToAttachment(SavedMedia m)
    {
        byte[] bytes = m.Bytes ?? [];
        return new ComposeAttachmentItem
        {
            Bytes = bytes,
            MimeType = m.MimeType,
            FileName = m.FileName,
            Kind = m.IsVideo ? ComposeAttachmentKind.Video : ComposeAttachmentKind.Image,
            PixelWidth = m.PixelWidth,
            PixelHeight = m.PixelHeight,
            // Over the blob limit and not shrunk, as it was when saved; Compress is offered again.
            IsOversized = !m.IsVideo && ComposeAttachmentPolicy.ValidateSize(ComposeAttachmentKind.Image, bytes.Length) is not null,
            AltText = m.AltText
        };
    }

    /// <summary>Everything in the box as a thread to keep: the first post, then each follow-on that has anything in it.</summary>
    private SavedThread BuildSavedThread()
    {
        var thread = new SavedThread
        {
            Id = _savedThreadId ?? Guid.NewGuid().ToString("N"),
            UpdatedAt = DateTimeOffset.UtcNow,
            AccountDid = BlueskySessionService.Instance.Did,
            AccountHandle = BlueskySessionService.Instance.Handle,
            Langs = [.. PostLanguages],
            QuoteUri = QuoteOf?.AtUri,
            QuoteCid = QuoteOf?.Reference?.Cid?.ToString(),
            LinkCard = _dismissedLinkUri is null,
            AllowQuotes = AllowQuotes
        };
        SavedThreadPolicy.SetReply(thread, ReplyTo is null ? ReplySetting : ReplyChoice.Everyone);

        thread.Posts.Add(new SavedPost
        {
            Text = Text.Trim(),
            Label = ContentLabel.ToString(),
            Media = Attachments.Select(ToSavedMedia).ToList()
        });

        foreach (ThreadPostItem item in ThreadPosts.Where(t => ThreadPostPolicy.IsSendable(t.Text, t.Attachments.Count)))
        {
            thread.Posts.Add(new SavedPost
            {
                Text = item.Text.Trim(),
                Label = item.ContentLabel.ToString(),
                Media = item.Attachments.Select(ToSavedMedia).ToList()
            });
        }

        return thread;
    }

    private static DraftItem ToLocalItem(SavedThread thread, DateTimeOffset now) => new()
    {
        Local = thread,
        Id = thread.BlueskyDraftId is { } id && TimestampIdentifier.TryParse(id, out TimestampIdentifier? parsed) ? parsed : null,
        Text = thread.Posts.FirstOrDefault()?.Text ?? string.Empty,
        Reply = SavedThreadPolicy.ReplyOf(thread),
        AllowQuotes = thread.AllowQuotes,
        Age = RelativeTimeFormatter.Format(thread.UpdatedAt, now),
        SortKey = thread.UpdatedAt,
        Summary = SavedThreadPolicy.Summary(thread),
        ScheduleText = SchedulePolicy.Status(thread, now, TimeZoneInfo.Local, CultureInfo.CurrentCulture),
        NeedsAttention = SchedulePolicy.NeedsAttention(thread, now),
        SortGroup = SchedulePolicy.SortGroup(thread, now),
        Note = SchedulePolicy.NeedsAttention(thread, now) ? SchedulePolicy.Reason(thread) : thread.LastError
    };

    /// <summary>
    /// Saves the box as a thread on this PC (and a text-only copy to Bluesky drafts). With
    /// <paramref name="scheduleAt"/> it is saved to be posted then, and only a copy on this PC will do,
    /// since that is what the scheduler posts. Returns null when saved, or what went wrong; a plain
    /// save also says so in <see cref="DraftsStatus"/>.
    /// </summary>
    private async Task<string?> SaveDraftCoreAsync(bool clearAfter, DateTimeOffset? scheduleAt = null)
    {
        DraftsStatus = null;

        SavedThread thread = BuildSavedThread();
        if (!SavedThreadPolicy.HasContent(thread))
        {
            DraftsStatus = "Write something first. A draft needs some text, media or a quoted post.";
            return DraftsStatus;
        }

        if (scheduleAt is { } at)
            SchedulePolicy.Schedule(thread, at);

        bool signedIn = BlueskySessionService.Instance.IsSignedIn;
        string[] texts = [.. thread.Posts.Select(p => p.Text)];
        StrongReference? quote = QuoteOf?.Reference;
        ReplyChoice reply = ReplyTo is null ? ReplySetting : ReplyChoice.Everyone;

        // A text-only copy goes to Bluesky drafts so bsky.app and other clients see it; the full thread
        // (with media) is kept on this PC, tied to that copy by its id.
        bool mirrored = false;
        if (signedIn && !PreviewMode.IsEnabled)
        {
            if (_draftId is { } existing && await DraftsService.UpdateAsync(existing, texts, quote, reply, AllowQuotes))
                mirrored = true;
            else if (await DraftsService.CreateAsync(texts, quote, reply, AllowQuotes) is { } created)
            {
                _draftId = created;
                mirrored = true;
            }
        }

        thread.BlueskyDraftId = mirrored ? _draftId?.ToString() : null;
        bool savedLocally = PreviewMode.IsEnabled || await SavedThreadStore.SaveAsync(thread);

        // The scheduler posts the copy on this PC; without one there is nothing to schedule. A Bluesky
        // copy made above stays an ordinary draft, and the box keeps the thread.
        if (scheduleAt is not null && !savedLocally)
            return "Couldn't schedule it: it couldn't be saved on this PC. What you wrote is still here.";

        if (!savedLocally && !mirrored)
        {
            DraftsStatus = "Couldn't save the draft. What you wrote is still here.";
            return DraftsStatus;
        }

        if (savedLocally)
            _savedThreadId = thread.Id;

        if (scheduleAt is { } scheduled)
        {
            ClearBox();
            ScheduledPostService.Instance.Refresh();
            LogService.Info("Schedule", $"Scheduled thread {thread.Id} ({thread.Posts.Count} posts) for {scheduled:o}");
            bool browserSession = BlueskySessionService.Instance.Agent.Credentials?.AuthenticationType == idunno.AtProto.Authentication.AuthenticationType.OAuth;
            string? sessionNote = SchedulePolicy.SessionNote(browserSession, scheduled, DateTimeOffset.Now);
            SetNotice($"Scheduled for {SchedulePolicy.Describe(scheduled, DateTimeOffset.Now, TimeZoneInfo.Local, CultureInfo.CurrentCulture)}. Traysky has to be running then. Change or cancel it from Drafts."
                + (sessionNote is null ? string.Empty : " " + sessionNote));
            return null;
        }

        string? note = SavedThreadPolicy.MirrorNote(SavedThreadPolicy.HasMedia(thread), ReplyTo is not null);
        string result = !savedLocally
            ? "Saved to Bluesky as text only; couldn't keep a copy on this PC."
            : !signedIn ? "Saved on this PC. Sign in to also keep it in your Bluesky drafts."
            : !mirrored ? "Saved on this PC. Couldn't reach Bluesky, so it isn't in your Bluesky drafts yet."
            : "Draft saved.";

        if (clearAfter)
            ClearBox();

        await RefreshDraftsAsync();
        DraftsStatus = note is null ? result : $"{result} {note}";
        return null;
    }

    /// <summary>Empties the box after its thread was set aside (saved and cleared, or scheduled).</summary>
    private void ClearBox()
    {
        _draftId = null;
        _savedThreadId = null;
        _rescheduleAt = null;
        Text = string.Empty;
        ReplyTo = null;
        QuoteOf = null;
        Attachments.Clear();
        ThreadPosts.Clear();
        ContentLabel = ComposeContentLabel.None;
        ReplySetting = ReplyChoice.Everyone;
        AllowQuotes = true;
        IsExpanded = false;
    }

    /// <summary>Raised when opening a draft would overwrite what's in the box; the view that has the Drafts flyout open asks, then calls <see cref="RestoreDraftAsync"/>.</summary>
    public event EventHandler<DraftItem>? RestoreConfirmationRequested;

    /// <summary>Why a saved thread can't go into the box right now, or null when it can.</summary>
    private static string? WhyNotOpen(SavedThread? thread) =>
        thread is null ? null
        : ScheduledPostService.Instance.IsPublishing(thread.Id) ? "It's being posted right now."
        : !SchedulePolicy.CanOpen(thread) ? "Part of this thread is already on Bluesky. Use Post now to send the rest, or delete it."
        : null;

    [RelayCommand]
    private async Task OpenDraftAsync(DraftItem draft)
    {
        if (WhyNotOpen(draft.Local) is { } why)
        {
            DraftsStatus = why;
            return;
        }

        // Restoring the draft the box already came from loses nothing, so only other content needs a say-so.
        bool sameDraft = ((draft.LocalId is not null && draft.LocalId == _savedThreadId) || (draft.Id is not null && Equals(draft.Id, _draftId)))
            && draft.Text == Text.Trim()
            && !HasThreadPosts;
        bool wouldOverwrite = (PostTextPolicy.HasDraftContent(Text, Attachments.Count) || HasThreadPosts) && !sameDraft;

        if (wouldOverwrite && RestoreConfirmationRequested is not null)
        {
            RestoreConfirmationRequested(this, draft);
            return;
        }

        await RestoreDraftAsync(draft);
    }

    /// <summary>Loads a draft into the box, replacing what's there. A draft that quoted a post that has since gone loses only the quote.</summary>
    public async Task RestoreDraftAsync(DraftItem draft)
    {
        DraftsStatus = null;

        // The copy on this PC has the media; a thread that exists only on Bluesky has just its text.
        SavedThread? local = draft.Local;
        DateTimeOffset? wasScheduledFor = null;
        if (local is not null && !PreviewMode.IsEnabled)
        {
            // Read again: the scheduler may have posted or tried it since the list was drawn.
            local = await SavedThreadStore.FindAsync(local.Id);
            if (local is null)
            {
                DraftsStatus = "It's no longer saved on this PC. It may have just been posted.";
                await ReloadLocalDraftsAsync();
                return;
            }

            if (WhyNotOpen(local) is { } why)
            {
                DraftsStatus = why;
                return;
            }

            // Off the schedule while it's in the box, so the scheduler can't post a copy that's being changed.
            if (local.ScheduledAt is { } at)
            {
                wasScheduledFor = at;
                SchedulePolicy.Unschedule(local);
                await SavedThreadStore.SaveDetailsAsync(local);
                ScheduledPostService.Instance.Refresh();
            }
        }

        bool mediaComplete = local is null || await SavedThreadStore.LoadMediaAsync(local);

        AtUri? quoteUri = local is not null
            ? (!string.IsNullOrEmpty(local.QuoteUri) && AtUri.TryParse(local.QuoteUri, out AtUri? parsedQuote) ? parsedQuote : null)
            : draft.Quote?.Uri;

        PostItem? quote = null;
        if (quoteUri is not null && !PreviewMode.IsEnabled)
        {
            quote = await FetchPostAsync(quoteUri);
            if (quote is null)
                DraftsStatus = "The post this draft quoted couldn't be loaded, so the quote was left out.";
        }

        ReplyTo = null;
        QuoteOf = quote;
        Attachments.Clear();
        ThreadPosts.Clear();
        ContentLabel = ComposeContentLabel.None;

        if (local is not null)
            ApplySavedThread(local);
        else
        {
            Text = draft.Text;
            foreach (string extra in draft.ExtraTexts)
                ThreadPosts.Add(new ThreadPostItem { Text = extra });
            ReplySetting = draft.Reply;
            AllowQuotes = draft.AllowQuotes;
        }

        _draftId = draft.Id;
        _savedThreadId = draft.LocalId;
        _rescheduleAt = wasScheduledFor;
        IsExpanded = true;
        Error = null;

        if (!mediaComplete)
            DraftsStatus ??= "Some of this draft's media couldn't be found on this PC, so it was left out.";

        // Said in the box rather than the list, so the Drafts flyout can close; typing clears it.
        if (wasScheduledFor is { } previous)
            SetNotice($"Taken off the schedule while you edit it (it was set for {SchedulePolicy.Describe(previous, DateTimeOffset.Now, TimeZoneInfo.Local, CultureInfo.CurrentCulture)}). Schedule it again when it's ready.");
        else if (DraftsStatus is null && draft.HasNote)
            DraftsStatus = draft.Note;

        DraftRestored?.Invoke(this, EventArgs.Empty);
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts a saved thread's posts, media, content warnings, languages and reply / quote controls into the box.</summary>
    private void ApplySavedThread(SavedThread thread)
    {
        SavedPost first = thread.Posts.FirstOrDefault() ?? new SavedPost();
        Text = first.Text;
        foreach (SavedMedia media in first.Media)
            Attachments.Add(ToAttachment(media));
        ContentLabel = SavedThreadPolicy.LabelOf(first);

        foreach (SavedPost post in thread.Posts.Skip(1))
        {
            var item = new ThreadPostItem { Text = post.Text };
            foreach (SavedMedia media in post.Media)
                item.Attachments.Add(ToAttachment(media));
            item.ContentLabel = SavedThreadPolicy.LabelOf(post);
            ThreadPosts.Add(item);
        }

        // This draft's languages apply to it without becoming the new default.
        IReadOnlyList<string> langs = ComposeLanguagePolicy.Parse(string.Join(',', thread.Langs));
        if (langs.Count > 0)
            PostLanguages = langs;

        ReplySetting = SavedThreadPolicy.ReplyOf(thread);
        AllowQuotes = thread.AllowQuotes;
    }

    /// <summary>Raised once a draft is in the box, so the Drafts flyout can close.</summary>
    public event EventHandler? DraftRestored;

    [RelayCommand]
    private async Task DeleteDraftAsync(DraftItem draft)
    {
        int index = Drafts.IndexOf(draft);
        if (index < 0)
            return;

        if (ScheduledPostService.Instance.IsPublishing(draft.LocalId))
        {
            DraftsStatus = "It's being posted right now.";
            return;
        }

        DraftsStatus = null;
        Drafts.RemoveAt(index);

        if (PreviewMode.IsEnabled || (draft.Id is null && draft.LocalId is null))
            return;

        // The Bluesky copy first: if that can't be removed, nothing is deleted and the row comes back.
        bool deleted = draft.Id is not { } remoteId || await DraftsService.DeleteAsync(remoteId);
        if (deleted && draft.LocalId is { } localId)
            deleted = await SavedThreadStore.DeleteAsync(localId);

        if (deleted)
        {
            if (draft.Id is not null && Equals(_draftId, draft.Id))
                _draftId = null;
            if (draft.LocalId is not null && draft.LocalId == _savedThreadId)
                _savedThreadId = null;
            if (draft.CanPostNow)
                ScheduledPostService.Instance.Refresh();
            return;
        }

        Drafts.Insert(Math.Min(index, Drafts.Count), draft);
        DraftsStatus = "Couldn't delete the draft. Try again in a moment.";
    }

    private static async Task<PostItem?> FetchPostAsync(AtUri uri)
    {
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            AtProtoHttpResult<IReadOnlyCollection<PostView>> posts =
                await BlueskySessionService.Instance.Agent.GetPosts([uri], LabelService.Instance.Subscribed);

            PostView? post = posts.Succeeded ? posts.Result?.FirstOrDefault() : null;
            return post is null ? null : FeedMapper.ToPostItem(post);
        }
        catch (Exception ex)
        {
            LogService.Warn("Compose", $"Fetching a draft's quoted post failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Follow-on posts of a thread, written under the first. Each has its own text, media and content warning.</summary>
    public ObservableCollection<ThreadPostItem> ThreadPosts { get; } = [];

    public bool HasThreadPosts => ThreadPosts.Count > 0;

    public bool CanAddThreadPost => ThreadPostPolicy.CanAdd(1 + ThreadPosts.Count);

    [RelayCommand(CanExecute = nameof(CanAddThreadPost))]
    private void AddThreadPost() => ThreadPosts.Add(new ThreadPostItem());

    [RelayCommand]
    private void RemoveThreadPost(ThreadPostItem item) => ThreadPosts.Remove(item);

    private void OnThreadPostsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ThreadPostItem old in e.OldItems)
                old.PropertyChanged -= OnThreadPostPropertyChanged;
        if (e.NewItems is not null)
            foreach (ThreadPostItem added in e.NewItems)
            {
                added.PropertyChanged -= OnThreadPostPropertyChanged; // an item can come back after a Clear
                added.PropertyChanged += OnThreadPostPropertyChanged;
            }

        for (int i = 0; i < ThreadPosts.Count; i++)
            ThreadPosts[i].Number = i + 2;

        OnPropertyChanged(nameof(HasThreadPosts));
        OnPropertyChanged(nameof(CanAddThreadPost));
        AddThreadPostCommand.NotifyCanExecuteChanged();
        PostCommand.NotifyCanExecuteChanged();
    }

    private void OnThreadPostPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThreadPostItem.Text) or nameof(ThreadPostItem.HasOversized))
            PostCommand.NotifyCanExecuteChanged();
    }

    private bool CanPost() => !IsPosting
        && ThreadPostPolicy.AllFit(ThreadPosts.Select(t => t.Text))
        && !ThreadPosts.Any(t => t.HasOversized)
        && !Attachments.Any(a => a.IsOversized)
        && (PostTextPolicy.CanPost(Text) || (HasAttachments && !PostTextPolicy.IsOverLimit(Text)));

    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
        {
            Error = "Sign in to post.";
            return;
        }

        IsPosting = true;
        Error = null;
        SetPhase(UploadPhase.Idle);
        string text = Text.Trim();
        string[] langs = [.. PostLanguages];
        // Snapshot the thread before any await: the text boxes stay editable while media uploads.
        List<(ThreadPostItem Item, string Text)> threadSnapshot = ThreadPosts
            .Where(t => ThreadPostPolicy.IsSendable(t.Text, t.Attachments.Count))
            .Select(t => (t, t.Text.Trim()))
            .ToList();
        var followOns = new List<FollowOnPost>(threadSnapshot.Count);
        ComposeContentLabel firstLabel = ContentLabel;
        BlueskyAgent agent = BlueskySessionService.Instance.Agent;
        bool isVideo = HasVideoAttachment;

        using var uploadCts = new CancellationTokenSource();
        _uploadCts = uploadCts;
        CancellationToken ct = uploadCts.Token;

        try
        {
            AtProtoHttpResult<CreateRecordResult> result;

            // Every post's media is uploaded before any record is created, so a failed or cancelled
            // upload leaves nothing half-posted.
            PreparedMedia? firstMedia = await PrepareMediaAsync(agent, Attachments.ToList(), ct);
            if (firstMedia is null)
                return;

            foreach ((ThreadPostItem item, string followText) in threadSnapshot)
            {
                PreparedMedia? media = await PrepareMediaAsync(agent, item.Attachments.ToList(), ct);
                if (media is null)
                    return;

                followOns.Add(new FollowOnPost(item, followText, media));
            }

            // Past this point the media is uploaded; cancelling is no longer offered and the records are created.
            SetPhase(UploadProgressPolicy.Complete(UploadPhase), isVideo);

            result = await PostPublisher.PostAsync(
                agent, text, langs, firstLabel, firstMedia,
                linkCard: HasLinkCard ? _linkCard : null,
                replyTo: ReplyTo?.Reference,
                quote: QuoteOf?.Reference);

            if (result.Succeeded)
            {
                LogService.Info("Compose", $"Posted {result.Result?.Uri}");
                if (ReplyTo is not null)
                    ReplyTo.ReplyCount++;
                if (QuoteOf is not null)
                    QuoteOf.RepostCount = QuoteOf.RepostCount; // quotes are counted separately; leave as-is

                // The gates are separate records keyed to the new post, so they follow the post going through.
                // A reply has no reply controls of its own: they belong to the thread's root.
                bool gatesApplied = true;
                string? newPostUri = result.Result?.Uri.ToString();
                if (newPostUri is not null && !InteractionGatePolicy.IsDefault(ReplyTo is null ? ReplySetting : ReplyChoice.Everyone, AllowQuotes))
                    gatesApplied = await GatesService.ApplyToNewPostAsync(newPostUri, ReplyTo is null ? ReplySetting : null, AllowQuotes);

                Text = string.Empty;
                ReplyTo = null;
                QuoteOf = null;
                Attachments.Clear();
                ContentLabel = ComposeContentLabel.None;
                ReplySetting = ReplyChoice.Everyone;
                AllowQuotes = true;
                IsExpanded = false;
                SetNotice(null);
                _rescheduleAt = null;

                // The rest of a thread goes out as replies chained to the post just sent.
                string? threadNote = null;
                if (followOns.Count > 0 && result.Result is { } first)
                {
                    int sent = await PostFollowOnsAsync(agent, new StrongReference(first.Uri, first.Cid), followOns, langs);
                    ThreadPosts.Clear();
                    if (sent < followOns.Count)
                    {
                        // What didn't go out stays in the box, as a fresh thread, so nothing typed or attached is lost:
                        // the first unsent post becomes the box's own post, the rest follow it.
                        FollowOnPost failed = followOns[sent];
                        Text = failed.Text;
                        foreach (ComposeAttachmentItem attachment in failed.Item.Attachments)
                            Attachments.Add(attachment);
                        ContentLabel = failed.Item.ContentLabel;
                        foreach (FollowOnPost rest in followOns.Skip(sent + 1))
                            ThreadPosts.Add(rest.Item);
                        IsExpanded = true;
                        threadNote = ThreadPostPolicy.PartialFailureNote(1 + sent, 1 + followOns.Count);
                    }
                }

                PostedCount++;
                Posted?.Invoke(this, EventArgs.Empty);

                // The draft this post grew from has served its purpose.
                if (_draftId is { } sentDraft)
                {
                    _draftId = null;
                    _ = DraftsService.DeleteAsync(sentDraft);
                }

                if (_savedThreadId is { } sentThread)
                {
                    _savedThreadId = null;
                    _ = SavedThreadStore.DeleteAsync(sentThread);
                }

                if (!gatesApplied)
                    Error = "Posted, but the reply or quote settings didn't apply. Use \"Reply and quote settings\" on the post to try again.";
                if (threadNote is not null)
                    Error = Error is null ? threadNote : $"{Error} {threadNote}";
                return;
            }

            LogService.Warn("Compose", $"Post failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error} {result.AtErrorDetail?.Message}");
            Error = result.StatusCode switch
            {
                System.Net.HttpStatusCode.TooManyRequests => "You're posting too fast. Try again in a minute.",
                0 => "Couldn't reach Bluesky. Your draft is safe.",
                _ => string.IsNullOrEmpty(result.AtErrorDetail?.Message) ? "Post failed. Your draft is safe." : result.AtErrorDetail.Message
            };
        }
        catch (OperationCanceledException)
        {
            // The user cancelled: not an error. Attachments stay so they can retry; any blobs already
            // uploaded are simply abandoned (the PDS garbage-collects unreferenced blobs).
            LogService.Info("Compose", "Upload cancelled");
            SetPhase(UploadProgressPolicy.Cancel(UploadPhase), isVideo);
        }
        catch (Exception ex)
        {
            LogService.Error("Compose", "Post threw", ex);
            Error = "Couldn't reach Bluesky. Your draft is safe.";
        }
        finally
        {
            // An early return with an error leaves the phase active; settle it so nothing reads "Uploading…".
            SetPhase(UploadProgressPolicy.Fail(UploadPhase), isVideo);
            _uploadCts = null;
            UploadProgress = null;
            IsPosting = false;
        }
    }

    /// <summary>A follow-on post ready to send: the item it came from, its trimmed text, and its uploaded media.</summary>
    private sealed record FollowOnPost(ThreadPostItem Item, string Text, PreparedMedia Media);

    internal static MediaUpload ToUpload(ComposeAttachmentItem a) =>
        new(a.Bytes, a.MimeType, a.FileName, a.IsVideo, a.AltText, a.PixelWidth, a.PixelHeight);

    /// <summary>
    /// Uploads one post's images, or its video (and waits for processing), showing progress in the box.
    /// Returns the blobs ready to embed (empty when there is no media), or null with <see cref="Error"/> set when something failed.
    /// </summary>
    private async Task<PreparedMedia?> PrepareMediaAsync(BlueskyAgent agent, IReadOnlyList<ComposeAttachmentItem> attachments, CancellationToken ct)
    {
        if (attachments.Count == 0)
            return PreparedMedia.None;

        bool isVideo = attachments.Any(a => a.IsVideo);
        SetPhase(UploadProgressPolicy.Begin(UploadPhase), isVideo);

        var reporter = new UploadReporter
        {
            Progress = p => UploadProgress = p,
            ProcessingStarted = () => SetPhase(UploadProgressPolicy.BeginProcessing(UploadPhase), isVideo: true)
        };

        MediaResult outcome = await PostPublisher.PrepareMediaAsync(agent, attachments.Select(ToUpload).ToList(), reporter, ct);
        if (outcome.Media is null)
            Error = outcome.Error;

        return outcome.Media;
    }

    /// <summary>Posts each text as a reply to the one before. Returns how many went out; stops at the first failure.</summary>
    private static async Task<int> PostFollowOnsAsync(BlueskyAgent agent, StrongReference parent, IReadOnlyList<FollowOnPost> posts, string[] langs)
    {
        int sent = 0;
        try
        {
            foreach (FollowOnPost post in posts)
            {
                AtProtoHttpResult<CreateRecordResult> result = await PostPublisher.PostAsync(
                    agent, post.Text, langs, post.Item.ContentLabel, post.Media, replyTo: parent);
                if (!result.Succeeded || result.Result is null)
                {
                    LogService.Warn("Compose", $"Thread post {sent + 2} failed: {(int)result.StatusCode} {result.AtErrorDetail?.Message}");
                    break;
                }

                parent = new StrongReference(result.Result.Uri, result.Result.Cid);
                sent++;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Compose", "Thread post threw", ex);
        }

        return sent;
    }
}

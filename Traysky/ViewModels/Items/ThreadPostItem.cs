using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One follow-on post of a thread being written in the compose box: its text, and its own images or
/// video and content warning, under the same rules as the first post.
/// </summary>
public sealed partial class ThreadPostItem : ObservableObject
{
    public ThreadPostItem()
    {
        Attachments.CollectionChanged += (_, _) =>
        {
            if (!HasAttachments)
                ContentLabel = ComposeContentLabel.None;

            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(HasVideoAttachment));
            OnPropertyChanged(nameof(CanAttachImage));
            OnPropertyChanged(nameof(CanAttachVideo));
            OnPropertyChanged(nameof(HasOversized));
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Counter))]
    [NotifyPropertyChangedFor(nameof(CounterBrushKey))]
    public partial string Text { get; set; } = string.Empty;

    public string Counter => $"{PostTextPolicy.CountGraphemes(Text)}/{PostTextPolicy.MaxGraphemes}";

    public string CounterBrushKey => ThreadPostPolicy.CounterBrushKey(Text);

    /// <summary>Position in the thread (the first post is 1), set by the compose box so the label reads "Post 2".</summary>
    [ObservableProperty]
    public partial int Number { get; set; }

    public string Label => $"Post {Number}";

    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(Label));

    /// <summary>Images or a video picked for this post, not yet uploaded. Images and video are mutually exclusive.</summary>
    public ObservableCollection<ComposeAttachmentItem> Attachments { get; } = [];

    public bool HasAttachments => Attachments.Count > 0;

    public bool HasVideoAttachment => Attachments.Any(a => a.IsVideo);

    public int ImageAttachmentCount => Attachments.Count(a => !a.IsVideo);

    public bool CanAttachImage => ComposeAttachmentPolicy.CanAddImage(ImageAttachmentCount, HasVideoAttachment);

    public bool CanAttachVideo => ComposeAttachmentPolicy.CanAddVideo(ImageAttachmentCount, HasVideoAttachment);

    /// <summary>An image over the size limit that hasn't been shrunk yet; the thread can't be sent until it is compressed or removed.</summary>
    public bool HasOversized => Attachments.Any(a => a.IsOversized);

    /// <summary>A content warning for this post's media; dropped when the media is removed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContentLabel))]
    [NotifyPropertyChangedFor(nameof(LabelBrushKey))]
    public partial ComposeContentLabel ContentLabel { get; set; }

    public bool HasContentLabel => ContentLabel != ComposeContentLabel.None;

    public string LabelBrushKey => HasContentLabel ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush";
}

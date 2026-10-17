using System;
using System.Text;

namespace Traysky.Services;

/// <summary>
/// Decisions for saved drafts. A draft carries text, a quoted post and the reply / quote controls;
/// picked media is held in memory and can't be stored in it, so these say so rather than dropping
/// it silently. Pure .NET on purpose.
/// </summary>
public static class DraftPolicy
{
    public const int PreviewLength = 90;

    /// <summary>Whether there is anything worth saving: text, a quoted post, or media that would otherwise be lost.</summary>
    public static bool CanSave(string? text, bool hasQuote, int attachmentCount) =>
        !string.IsNullOrWhiteSpace(text) || hasQuote || attachmentCount > 0;

    /// <summary>A draft with no text and nothing else saved would be empty; media alone isn't enough to store.</summary>
    public static bool HasStorableContent(string? text, bool hasQuote) =>
        !string.IsNullOrWhiteSpace(text) || hasQuote;

    /// <summary>The first line of the draft squeezed onto one line for a list row, cut with an ellipsis when long.</summary>
    public static string Preview(string? text, bool hasQuote = false)
    {
        string flat = FlattenWhitespace(text);
        if (flat.Length == 0)
            return hasQuote ? "(quote with no comment)" : "(empty draft)";

        return flat.Length <= PreviewLength ? flat : string.Concat(flat.AsSpan(0, PreviewLength).TrimEnd(), "…");
    }

    /// <summary>
    /// Shown after saving: what the draft lacks. Picked media can't be stored, and neither can the post being
    /// replied to (a draft is a standalone post). Null when nothing is lost.
    /// </summary>
    public static string? SaveNote(int imageCount, bool hasVideo, bool isReply = false)
    {
        string? media = hasVideo
            ? "The video isn't saved with a draft. Attach it again when you restore it."
            : imageCount == 1
                ? "The image isn't saved with a draft. Attach it again when you restore it."
                : imageCount > 1
                    ? $"The {imageCount} images aren't saved with a draft. Attach them again when you restore it."
                    : null;

        string? reply = isReply ? "The post you're replying to isn't saved with it either." : null;

        return (media, reply) switch
        {
            (null, null) => null,
            (not null, null) => media,
            (null, not null) => reply,
            _ => $"{media} {reply}"
        };
    }

    /// <summary>Shown on a draft written elsewhere that has media this app can't restore. Null when it has none.</summary>
    public static string? RestoreNote(int imageCount, int videoCount)
    {
        int media = imageCount + videoCount;
        if (media == 0)
            return null;

        string what = videoCount > 0 && imageCount > 0 ? "media" : videoCount > 0 ? (videoCount == 1 ? "video" : "videos") : (imageCount == 1 ? "image" : "images");
        string count = media == 1 ? "1" : media.ToString();
        return $"This draft had {count} {what} that can't be restored here, only its text.";
    }

    /// <summary>Whether a draft was saved from this machine, going by the device name stored with it.</summary>
    public static bool IsFromThisDevice(string? draftDeviceId, string thisDeviceId) =>
        !string.IsNullOrEmpty(draftDeviceId) && string.Equals(draftDeviceId, thisDeviceId, StringComparison.Ordinal);

    public static string DeviceId(string machineName) => $"traysky:{machineName.Trim().ToLowerInvariant()}";

    private static string FlattenWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        StringBuilder builder = new(text.Length);
        bool pendingSpace = false;
        foreach (char c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(c);
        }
        return builder.ToString();
    }
}

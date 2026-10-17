using System;
using System.Collections.Generic;

namespace Traysky.Services;

public enum ComposeAttachmentKind
{
    Image,
    Video
}

/// <summary>
/// Bluesky's rules for what a post's media embed can look like: up to <see cref="MaxImages"/>
/// images, or exactly one video, never both. Pure .NET so the limits are testable without a
/// WinRT dependency; the extension→mime maps double as the file picker's type filter and the
/// "is this a supported file" check after picking.
/// </summary>
public static class ComposeAttachmentPolicy
{
    public const int MaxImages = 4;

    /// <summary>The PDS blob size limit Bluesky enforces for post images (~976 KB).</summary>
    public const long MaxImageBytes = 1_000_000;

    /// <summary>A generous ceiling; the video pipeline's own upload-limits check is authoritative.</summary>
    public const long MaxVideoBytes = 100_000_000;

    private static readonly IReadOnlyDictionary<string, string> ImageMimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    private static readonly IReadOnlyDictionary<string, string> VideoMimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
    };

    public static IEnumerable<string> ImageExtensions => ImageMimeTypes.Keys;

    public static IEnumerable<string> VideoExtensions => VideoMimeTypes.Keys;

    /// <summary>The mime type for a file extension (with leading dot), or null if it isn't a supported image or video type.</summary>
    public static string? MimeTypeFor(string extension)
    {
        if (ImageMimeTypes.TryGetValue(extension, out string? image))
            return image;

        return VideoMimeTypes.TryGetValue(extension, out string? video) ? video : null;
    }

    public static bool IsImageExtension(string extension) => ImageMimeTypes.ContainsKey(extension);

    public static bool IsVideoExtension(string extension) => VideoMimeTypes.ContainsKey(extension);

    /// <summary>Images and video are mutually exclusive, and a post can carry at most <see cref="MaxImages"/> images.</summary>
    public static bool CanAddImage(int currentImageCount, bool hasVideo) => !hasVideo && currentImageCount < MaxImages;

    /// <summary>A video can only be added to a post that has no images and no video already.</summary>
    public static bool CanAddVideo(int currentImageCount, bool hasVideo) => !hasVideo && currentImageCount == 0;

    /// <summary>
    /// Checks a video against the account's daily video-service allowance (from <c>getUploadLimits</c>).
    /// A null remaining value means no limit is imposed. Returns a user-facing message when the video
    /// can't go up, otherwise null.
    /// </summary>
    public static string? ValidateVideoLimits(bool canUpload, long? remainingVideos, long? remainingBytes, long sizeBytes)
    {
        if (!canUpload)
            return "Video uploads aren't available for this account right now.";
        if (remainingVideos is <= 0)
            return "You've reached today's video upload limit. Try again tomorrow.";
        if (remainingBytes is { } bytes && sizeBytes > bytes)
            return bytes <= 0
                ? "You've reached today's video upload limit. Try again tomorrow."
                : $"This video is larger than the {FormatMegabytes(bytes)} you have left to upload today.";
        return null;
    }

    /// <summary>"3 videos, 120 MB left today", or null when the account has no daily limit to show.</summary>
    public static string? LimitsSummary(long? remainingVideos, long? remainingBytes)
    {
        string? videos = remainingVideos is { } v ? $"{Math.Max(v, 0)} video{(v == 1 ? string.Empty : "s")}" : null;
        string? size = remainingBytes is { } b ? FormatMegabytes(Math.Max(b, 0)) : null;
        return (videos, size) switch
        {
            (null, null) => null,
            (not null, null) => $"{videos} left today",
            (null, not null) => $"{size} left today",
            _ => $"{videos}, {size} left today"
        };
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / 1_000_000.0:0.#} MB";

    /// <summary>Returns a user-facing error message if <paramref name="sizeBytes"/> exceeds the limit for <paramref name="kind"/>, otherwise null.</summary>
    public static string? ValidateSize(ComposeAttachmentKind kind, long sizeBytes) => kind switch
    {
        ComposeAttachmentKind.Image when sizeBytes > MaxImageBytes => $"Image is too large (max {MaxImageBytes / 1_000_000.0:0.#} MB).",
        ComposeAttachmentKind.Video when sizeBytes > MaxVideoBytes => $"Video is too large (max {MaxVideoBytes / 1_000_000} MB).",
        _ => null
    };
}

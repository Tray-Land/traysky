using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Traysky.Services;

/// <summary>What the profile editor should do with the avatar or banner when saving.</summary>
public enum ProfileImageChange
{
    Keep,
    Replace,
    Remove
}

/// <summary>
/// Limits and rules for editing the account's own profile. Pure .NET; the record read/write and
/// the image encoding live in <c>ProfileEditService</c> and <c>ImageCompressor</c>. The lexicon
/// caps display name at 64 graphemes / 640 bytes and the bio at 256 graphemes / 2560 bytes.
/// </summary>
public static class ProfileEditPolicy
{
    public const int DisplayNameMaxGraphemes = 64;
    public const int DisplayNameMaxBytes = 640;
    public const int BioMaxGraphemes = 256;
    public const int BioMaxBytes = 2560;

    /// <summary>The blob limit for both the avatar and the banner.</summary>
    public const long MaxImageBytes = 1_000_000;

    /// <summary>Avatars are shown small and square-cropped, so shrinking hard costs little.</summary>
    public static IReadOnlyList<ImageCompressionPolicy.Step> AvatarSteps { get; } =
    [
        new(1024, 0.90),
        new(1024, 0.75),
        new(800, 0.65),
        new(640, 0.55),
        new(512, 0.45),
    ];

    public static IReadOnlyList<ImageCompressionPolicy.Step> BannerSteps { get; } =
    [
        new(3000, 0.85),
        new(2000, 0.75),
        new(1600, 0.65),
        new(1280, 0.55),
        new(1000, 0.45),
    ];

    public static int GraphemeCount(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : new StringInfo(text).LengthInTextElements;

    /// <summary>Why the display name can't be saved, or null when it can. Empty is allowed: the handle shows instead.</summary>
    public static string? DisplayNameError(string? name) =>
        TooLong(name, DisplayNameMaxGraphemes, DisplayNameMaxBytes, "Display name");

    /// <summary>Why the bio can't be saved, or null when it can.</summary>
    public static string? BioError(string? bio) =>
        TooLong(bio, BioMaxGraphemes, BioMaxBytes, "Bio");

    public static string? Validate(string? name, string? bio) => DisplayNameError(name) ?? BioError(bio);

    /// <summary>Text for a save: trimmed, and null (field removed) when nothing is left.</summary>
    public static string? Normalize(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>"12 / 64" style counter shown under a field.</summary>
    public static string Counter(string? text, int maxGraphemes) => $"{GraphemeCount(text)} / {maxGraphemes}";

    /// <summary>Whether anything differs from the loaded profile, so Save can stay disabled until it does.</summary>
    public static bool HasChanges(string? originalName, string? originalBio, string? name, string? bio,
        ProfileImageChange avatar, ProfileImageChange banner) =>
        Normalize(originalName) != Normalize(name)
        || Normalize(originalBio) != Normalize(bio)
        || avatar != ProfileImageChange.Keep
        || banner != ProfileImageChange.Keep;

    private static string? TooLong(string? text, int maxGraphemes, int maxBytes, string label)
    {
        string value = (text ?? string.Empty).Trim();
        if (GraphemeCount(value) > maxGraphemes)
            return $"{label} can be at most {maxGraphemes} characters.";
        if (Encoding.UTF8.GetByteCount(value) > maxBytes)
            return $"{label} is too long.";
        return null;
    }
}

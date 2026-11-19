using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>The self-label an author puts on a post's media, as bsky.app's "Add a content warning" offers.</summary>
public enum ComposeContentLabel
{
    None,
    Suggestive,
    Nudity,
    Adult,
    Graphic
}

/// <summary>Maps the composer's content-warning choices to the label values Bluesky records. Pure so it can be tested.</summary>
public static class ContentLabelPolicy
{
    public static IReadOnlyList<ComposeContentLabel> Choices { get; } =
    [
        ComposeContentLabel.None,
        ComposeContentLabel.Suggestive,
        ComposeContentLabel.Nudity,
        ComposeContentLabel.Adult,
        ComposeContentLabel.Graphic
    ];

    /// <summary>The self-label value to write, or null for none.</summary>
    public static string? Value(ComposeContentLabel label) => label switch
    {
        ComposeContentLabel.Suggestive => "sexual",
        ComposeContentLabel.Nudity => "nudity",
        ComposeContentLabel.Adult => "porn",
        ComposeContentLabel.Graphic => "graphic-media",
        _ => null
    };

    public static string Title(ComposeContentLabel label) => label switch
    {
        ComposeContentLabel.Suggestive => "Suggestive",
        ComposeContentLabel.Nudity => "Nudity",
        ComposeContentLabel.Adult => "Porn",
        ComposeContentLabel.Graphic => "Graphic media",
        _ => "No label"
    };

    public static string Description(ComposeContentLabel label) => label switch
    {
        ComposeContentLabel.Suggestive => "Sexual but not explicit",
        ComposeContentLabel.Nudity => "Artistic or non-erotic nudity",
        ComposeContentLabel.Adult => "Explicit sexual content or nudity (18+)",
        ComposeContentLabel.Graphic => "Violence or gore",
        _ => "Nothing to warn about"
    };

    /// <summary>Labels cover a post's images or video; a text-only post has nothing to put one on.</summary>
    public static bool CanLabel(bool hasMedia) => hasMedia;

    /// <summary>The label that applies to a post as sent: dropped when the media it was for is gone.</summary>
    public static ComposeContentLabel Effective(ComposeContentLabel label, bool hasMedia) =>
        hasMedia ? label : ComposeContentLabel.None;
}

using System;
using System.Text.RegularExpressions;

namespace Traysky.Services;

/// <summary>
/// Decisions for the compose box's link preview card: which URL in the draft gets a card, and
/// where a just-uploaded card thumbnail can be previewed from. Pure .NET on purpose.
/// </summary>
public static partial class LinkCardPolicy
{
    /// <summary>How long typing has to pause before a link is looked up, so half-typed URLs aren't fetched.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(700);

    private const string TrailingPunctuation = ".,;:!?)]}>'\"";

    [GeneratedRegex(@"https?://[^\s<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    /// <summary>
    /// The first http(s) link in <paramref name="text"/>, minus trailing sentence punctuation, or
    /// null. Like bsky.app, only the first link gets a card.
    /// </summary>
    public static Uri? FirstLink(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        foreach (Match match in UrlRegex().Matches(text))
        {
            string candidate = match.Value.TrimEnd(TrailingPunctuation.ToCharArray());
            if (BlueskyLinks.TryParseWebUri(candidate, out Uri? uri) && !string.IsNullOrEmpty(uri.Host) && uri.Host.Contains('.'))
                return uri;
        }

        return null;
    }

    /// <summary>
    /// Bluesky's image CDN URL for a blob the user's own repo holds, used to preview a card
    /// thumbnail that has been uploaded but not yet attached to a post.
    /// </summary>
    public static Uri? ThumbnailPreviewUri(string? did, string? cid) =>
        string.IsNullOrEmpty(did) || string.IsNullOrEmpty(cid)
            ? null
            : new Uri($"https://cdn.bsky.app/img/feed_thumbnail/plain/{did}/{cid}@jpeg");

    /// <summary>A card rides in the post's single embed slot, so images, video and quotes win over it.</summary>
    public static bool CanAttachCard(bool hasAttachments, bool hasQuote) => !hasAttachments && !hasQuote;
}

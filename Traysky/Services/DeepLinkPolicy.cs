using System;

namespace Traysky.Services;

public enum DeepLinkKind
{
    Compose,
    Profile,
    Post,
    Hashtag,
    JoinGroup
}

/// <summary>What a <c>traysky:</c> link asks for. Only the fields its <see cref="Kind"/> uses are set.</summary>
public sealed record DeepLink(DeepLinkKind Kind, string Actor = "", string Rkey = "", string Text = "")
{
    /// <summary>The AT URI of the post, for <see cref="DeepLinkKind.Post"/>.</summary>
    public string AtUri => $"at://{Actor}/app.bsky.feed.post/{Rkey}";
}

/// <summary>
/// The <c>traysky:</c> URI scheme other apps, scripts and browsers can launch Traysky with.
/// Both <c>traysky://profile/alice.bsky.social</c> and <c>traysky:profile/alice.bsky.social</c>
/// forms are accepted. No WinRT dependency, so it is linked into the tests.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>traysky:compose?text=Hello&amp;url=https://example.com</c> opens the compose box with that text (and url on its own line) added to the draft.</item>
/// <item><c>traysky:profile/{handle-or-did}</c> opens a profile.</item>
/// <item><c>traysky:post/{handle-or-did}/{rkey}</c> or <c>traysky:post?uri=at://…</c> opens a post's thread.</item>
/// <item><c>traysky:hashtag/{tag}</c> opens a hashtag search.</item>
/// <item><c>traysky:join/{code}</c> previews a group chat's join link and offers to join it.</item>
/// </list>
/// The scheme is declared as a protocol in Package.appxmanifest; the two must agree.
/// </remarks>
public static class DeepLinkPolicy
{
    public const string Scheme = "traysky";

    public static bool TryParse(Uri? uri, out DeepLink? link)
    {
        link = null;
        if (uri is null || !uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        // traysky://profile/x puts "profile" in the host; traysky:profile/x puts it in the path.
        string route = uri.Host.Length > 0 ? uri.Host + uri.AbsolutePath : uri.AbsolutePath;
        string[] segments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        for (int i = 0; i < segments.Length; i++)
            segments[i] = Uri.UnescapeDataString(segments[i]);

        switch (segments[0].ToLowerInvariant())
        {
            case "compose":
                string text = OAuthCallbackPolicy.GetQueryValue(uri, "text")?.Trim() ?? string.Empty;
                string url = OAuthCallbackPolicy.GetQueryValue(uri, "url")?.Trim() ?? string.Empty;
                link = new DeepLink(DeepLinkKind.Compose, Text: ShareTargetPolicy.ComposeText(null, text, url));
                return true;

            case "profile" when segments.Length >= 2:
                link = new DeepLink(DeepLinkKind.Profile, Actor: BlueskyLinks.NormalizeHandle(segments[1]));
                return link.Actor.Length > 0;

            case "post" when segments.Length >= 3:
                link = new DeepLink(DeepLinkKind.Post, Actor: BlueskyLinks.NormalizeHandle(segments[1]), Rkey: segments[2]);
                return true;

            case "post":
                string? atUri = OAuthCallbackPolicy.GetQueryValue(uri, "uri");
                if (BlueskyLinks.TryParseAtUri(atUri, out string authority, out string collection, out string rkey)
                    && collection == "app.bsky.feed.post" && rkey.Length > 0)
                {
                    link = new DeepLink(DeepLinkKind.Post, Actor: authority, Rkey: rkey);
                    return true;
                }

                return false;

            case "hashtag" when segments.Length >= 2:
                link = new DeepLink(DeepLinkKind.Hashtag, Text: segments[1].TrimStart('#'));
                return link.Text.Length > 0;

            case "join" when segments.Length >= 2:
                string? code = GroupChatPolicy.ParseJoinCode(segments[1]);
                if (code is null)
                    return false;

                link = new DeepLink(DeepLinkKind.JoinGroup, Text: code);
                return true;

            default:
                return false;
        }
    }
}

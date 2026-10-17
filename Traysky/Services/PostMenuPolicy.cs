using System;

namespace Traysky.Services;

/// <summary>Which optional items the post "…" menu offers. Pure .NET on purpose.</summary>
public readonly record struct PostMenuState(
    bool ShowDelete,
    bool ShowAccountModeration,
    bool ShowMuteThread,
    string MuteThreadLabel,
    bool ShowInteractionSettings = false);

public static class PostMenuPolicy
{
    public static PostMenuState For(string authorDid, string? sessionDid, bool signedIn, bool threadMuted)
    {
        bool own = signedIn
            && !string.IsNullOrEmpty(sessionDid)
            && string.Equals(authorDid, sessionDid, StringComparison.Ordinal);

        return new PostMenuState(
            ShowDelete: own,
            // Muting, blocking and reporting yourself makes no sense.
            ShowAccountModeration: signedIn && !own,
            ShowMuteThread: signedIn,
            MuteThreadLabel: threadMuted ? "Unmute thread" : "Mute thread",
            ShowInteractionSettings: InteractionGatePolicy.CanEditQuoteSettings(own));
    }

    /// <summary>The thread's root post: the reply's root when it is a reply, else the post itself.</summary>
    public static string ThreadRootUri(string postUri, string? replyRootUri) =>
        string.IsNullOrEmpty(replyRootUri) ? postUri : replyRootUri;
}

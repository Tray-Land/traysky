using System;

namespace Traysky.Services;

/// <summary>Which optional items the post "…" menu offers. Pure .NET on purpose.</summary>
public readonly record struct PostMenuState(
    bool ShowDelete,
    bool ShowAccountModeration,
    bool ShowMuteThread,
    string MuteThreadLabel,
    bool ShowInteractionSettings = false,
    bool ShowDetachQuote = false);

public static class PostMenuPolicy
{
    /// <param name="quotedPostUri">AT URI of the post this one quotes, if any and still attached; offers detaching when it is the viewer's.</param>
    public static PostMenuState For(string authorDid, string? sessionDid, bool signedIn, bool threadMuted, string? quotedPostUri = null)
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
            ShowInteractionSettings: InteractionGatePolicy.CanEditQuoteSettings(own),
            // Detaching your own quote of your own post is pointless; deleting it does that.
            ShowDetachQuote: !own && DetachQuotePolicy.CanDetach(quotedPostUri, sessionDid, signedIn));
    }

    /// <summary>The thread's root post: the reply's root when it is a reply, else the post itself.</summary>
    public static string ThreadRootUri(string postUri, string? replyRootUri) =>
        string.IsNullOrEmpty(replyRootUri) ? postUri : replyRootUri;
}

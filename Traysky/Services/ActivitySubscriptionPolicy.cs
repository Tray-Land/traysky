using System;

namespace Traysky.Services;

/// <summary>How much of one account's activity you are notified about.</summary>
public enum ActivitySubscriptionLevel
{
    Off,
    Posts,
    PostsAndReplies
}

/// <summary>
/// The bell on a profile: who may be subscribed to, and how the three choices map to the API's
/// <c>post</c> / <c>reply</c> flags. Pure .NET on purpose.
/// </summary>
public static class ActivitySubscriptionPolicy
{
    public static ActivitySubscriptionLevel FromFlags(bool posts, bool replies) =>
        !posts ? ActivitySubscriptionLevel.Off
        : replies ? ActivitySubscriptionLevel.PostsAndReplies
        : ActivitySubscriptionLevel.Posts;

    /// <summary>The API refuses replies without posts, so a level always maps to a valid pair.</summary>
    public static (bool Posts, bool Replies) ToFlags(ActivitySubscriptionLevel level) => level switch
    {
        ActivitySubscriptionLevel.Posts => (true, false),
        ActivitySubscriptionLevel.PostsAndReplies => (true, true),
        _ => (false, false)
    };

    public static string Label(ActivitySubscriptionLevel level) => level switch
    {
        ActivitySubscriptionLevel.Posts => "Posts",
        ActivitySubscriptionLevel.PostsAndReplies => "Posts and replies",
        _ => "Off"
    };

    /// <summary>Menu text and tooltip for the bell button.</summary>
    public static string BellTooltip(ActivitySubscriptionLevel level) => level == ActivitySubscriptionLevel.Off
        ? "Get notified about their posts"
        : $"Notifications on: {Label(level).ToLowerInvariant()}";

    /// <summary>
    /// Whether this account accepts your subscription. <paramref name="allowSubscriptions"/> is the
    /// profile's <c>associated.activitySubscription.allowSubscriptions</c>: "followers" (also the
    /// default when absent), "mutuals" or "none". A value we do not know is treated as closed.
    /// </summary>
    public static bool CanSubscribe(string? allowSubscriptions, bool isSelf, bool following, bool followedBy)
    {
        if (isSelf)
            return false;

        if (string.IsNullOrEmpty(allowSubscriptions) || string.Equals(allowSubscriptions, "followers", StringComparison.OrdinalIgnoreCase))
            return following;

        if (string.Equals(allowSubscriptions, "mutuals", StringComparison.OrdinalIgnoreCase))
            return following && followedBy;

        return false;
    }

    /// <summary>
    /// The bell shows when you can subscribe, and also whenever you already are (even if the account
    /// has since closed subscriptions), so there is always a way to turn it off.
    /// </summary>
    public static bool ShowBell(string? allowSubscriptions, bool isSelf, bool following, bool followedBy, ActivitySubscriptionLevel current) =>
        !isSelf && (current != ActivitySubscriptionLevel.Off || CanSubscribe(allowSubscriptions, isSelf, following, followedBy));
}

using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>The server-side notification types Traysky lets you tune. Chat has its own rows in <see cref="ChatNotificationPreferencePolicy"/>.</summary>
public enum NotificationPrefType
{
    Like,
    LikeViaRepost,
    Repost,
    RepostViaRepost,
    Follow,
    Reply,
    Mention,
    Quote,
    StarterPackJoined,
    SubscribedPost,
    Verified
}

/// <summary>One line of the notification settings screen.</summary>
public sealed record NotificationPrefRow(NotificationPrefType Type, string Label, string Description, bool CanLimitToFollows);

/// <summary>
/// Which notification types the settings screen shows, how they read, and which of them can be
/// limited to people you follow. Pure .NET so the mapping to Bluesky's preference record lives
/// next to the view model that owns the idunno types.
/// </summary>
public static class NotificationPreferencePolicy
{
    public static IReadOnlyList<NotificationPrefRow> Rows { get; } =
    [
        new(NotificationPrefType.Reply, "Replies", "Someone replies to one of your posts.", true),
        new(NotificationPrefType.Mention, "Mentions", "Someone mentions you in a post.", true),
        new(NotificationPrefType.Quote, "Quotes", "Someone quotes one of your posts.", true),
        new(NotificationPrefType.Like, "Likes", "Someone likes one of your posts.", true),
        new(NotificationPrefType.LikeViaRepost, "Likes of your reposts", "Someone likes a post you reposted.", true),
        new(NotificationPrefType.Repost, "Reposts", "Someone reposts one of your posts.", true),
        new(NotificationPrefType.RepostViaRepost, "Reposts of your reposts", "Someone reposts a post you reposted.", true),
        new(NotificationPrefType.Follow, "New followers", "Someone follows you.", true),
        new(NotificationPrefType.SubscribedPost, "Activity from accounts you follow closely", "An account you turned its bell on for posts or replies.", false),
        new(NotificationPrefType.StarterPackJoined, "Starter pack joins", "Someone joins Bluesky through your starter pack.", false),
        new(NotificationPrefType.Verified, "Verification", "You are verified by a trusted account.", false)
    ];

    public static NotificationPrefRow RowFor(NotificationPrefType type)
    {
        foreach (NotificationPrefRow row in Rows)
        {
            if (row.Type == type)
                return row;
        }
        return Rows[0];
    }

    /// <summary>"In your list and pushed, from people you follow" - the row's current state in words.</summary>
    public static string Summary(bool showInList, bool push, bool followsOnly)
    {
        if (!showInList && !push)
            return "Off";

        string where = showInList && push ? "In your list and pushed" : showInList ? "In your list only" : "Pushed only";
        return followsOnly ? where + ", from people you follow" : where;
    }
}

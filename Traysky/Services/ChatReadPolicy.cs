namespace Traysky.Services;

/// <summary>Which list the Messages page shows: everything, or just the message requests.</summary>
public enum ChatListMode
{
    All,
    Requests
}

/// <summary>
/// Decisions for the Messages page's read-all button and its Requests filter. Pure .NET on purpose.
/// </summary>
public static class ChatReadPolicy
{
    /// <summary>"Mark all read" is enabled only in the main list and only while something is unread.</summary>
    public static bool CanMarkAllRead(ChatListMode mode, int badgeCount, int unreadAcceptedInList) =>
        mode == ChatListMode.All && (badgeCount > 0 || unreadAcceptedInList > 0);

    /// <summary>Only the main list feeds the Messages badge; the Requests list holds nothing that counts toward it.</summary>
    public static bool UpdatesBadge(ChatListMode mode) => mode == ChatListMode.All;

    public static string EmptyText(ChatListMode mode) => mode == ChatListMode.Requests
        ? "No message requests."
        : "No conversations yet. Start one with the Message button on someone's profile.";

    /// <summary>The Requests tab label, with how many are loaded ("30+" while more pages remain).</summary>
    public static string RequestsLabel(int loaded, bool hasMore)
    {
        if (loaded <= 0)
            return "Requests";

        return hasMore ? $"Requests ({loaded}+)" : $"Requests ({loaded})";
    }
}

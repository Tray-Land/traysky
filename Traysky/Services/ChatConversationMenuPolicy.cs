namespace Traysky.Services;

/// <summary>What the conversation "..." menu offers. Pure .NET on purpose.</summary>
public readonly record struct ChatConversationMenu(
    bool ShowAccept,
    bool ShowMute,
    bool ShowUnmute,
    string LeaveLabel,
    string LeavePrompt,
    string LeaveButton,
    bool ShowGroupInfo = false);

public static class ChatConversationMenuPolicy
{
    public static ChatConversationMenu For(bool isRequest, bool isMuted, bool isGroup)
    {
        // A request is declined, not left: the other side isn't told either way, but it reads right.
        // Muting a request makes little sense - it isn't in the inbox yet.
        string noun = isGroup ? "group" : "conversation";
        return isRequest
            ? new ChatConversationMenu(true, false, false, "Decline request", "Decline this message request? It will be removed.", "Decline")
            : new ChatConversationMenu(false, !isMuted, isMuted, $"Leave {noun}", $"Leave this {noun}? It disappears from your list; the {(isGroup ? "others keep" : "other side keeps")} their copy.", "Leave", ShowGroupInfo: isGroup);
    }

    /// <summary>
    /// The unread figure the Messages badge shows: accepted conversations only, since requests
    /// are not in the inbox until accepted. The server caps the figure at 100.
    /// </summary>
    public static int BadgeCount(int unreadAccepted) => unreadAccepted < 0 ? 0 : unreadAccepted;
}

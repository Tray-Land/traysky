namespace Traysky.Services;

/// <summary>The two kinds of chat notification Bluesky lets you tune.</summary>
public enum ChatPrefType
{
    Chat,
    ChatRequest
}

/// <summary>One line of the chat part of the notification settings screen.</summary>
public sealed record ChatPrefRow(ChatPrefType Type, string Label, string Description);

/// <summary>
/// The chat notification options. Unlike the other notification types these have no "in your
/// list" switch, only whether to push and whether to limit it to people you follow. Pure .NET.
/// </summary>
public static class ChatNotificationPreferencePolicy
{
    public static System.Collections.Generic.IReadOnlyList<ChatPrefRow> Rows { get; } =
    [
        new(ChatPrefType.Chat, "Messages", "New messages in conversations you've accepted."),
        new(ChatPrefType.ChatRequest, "Message requests", "Someone you don't follow starts a conversation with you.")
    ];

    /// <summary>A preference the server did not send reads as Bluesky's default: pushed, from everyone.</summary>
    public const bool DefaultPush = true;

    public const bool DefaultFollowsOnly = false;

    public static string Summary(bool push, bool followsOnly)
    {
        if (!push)
            return "Off";

        return followsOnly ? "Pushed, from people you follow" : "Pushed, from everyone";
    }
}

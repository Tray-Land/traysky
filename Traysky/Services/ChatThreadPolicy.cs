using System;
using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>
/// Layout decisions for a conversation's message list. Pure .NET on purpose.
/// </summary>
public static class ChatThreadPolicy
{
    /// <summary>Messages from the same sender closer together than this read as one burst.</summary>
    public static readonly TimeSpan RunGap = TimeSpan.FromMinutes(5);

    /// <summary>The most characters one message may carry (chat.bsky.convo.defs#messageInput allows 1000 graphemes).</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>
    /// True when a message starts a new visual run: the first message, a different sender, or a
    /// long enough pause. The first message of a run shows the timestamp (and, in a group, the
    /// sender's name); the rest stack tightly under it.
    /// </summary>
    public static bool StartsRun(string? previousSenderDid, DateTimeOffset? previousSentAt, string senderDid, DateTimeOffset sentAt) =>
        previousSenderDid is null
        || previousSentAt is null
        || !string.Equals(previousSenderDid, senderDid, StringComparison.Ordinal)
        || sentAt - previousSentAt.Value > RunGap;

    /// <summary>Whether the send button should be enabled for <paramref name="draft"/>.</summary>
    public static bool CanSend(string? draft) =>
        !string.IsNullOrWhiteSpace(draft) && draft.Trim().Length <= MaxMessageLength;

    /// <summary>
    /// Folds a batch of conversation log entries into what the open conversation has to do.
    /// Entries for other conversations only say the inbox changed. A message created and then
    /// deleted inside the batch is never added.
    /// </summary>
    public static ChatLogDelta Reduce(IEnumerable<ChatLogEntry> entries, string conversationId, IReadOnlySet<string> knownMessageIds)
    {
        List<string> added = [];
        List<string> deleted = [];
        bool inboxChanged = false;
        bool reactions = false;
        bool left = false;
        bool groupChanged = false;
        bool? muted = null;

        foreach (ChatLogEntry entry in entries)
        {
            bool mine = string.Equals(entry.ConversationId, conversationId, StringComparison.Ordinal);

            if (entry.Kind is ChatLogKind.MessageCreated or ChatLogKind.MessageDeleted or ChatLogKind.Muted
                or ChatLogKind.Unmuted or ChatLogKind.Left or ChatLogKind.Began or ChatLogKind.GroupChanged)
            {
                inboxChanged = true;
            }

            if (!mine)
                continue;

            switch (entry.Kind)
            {
                case ChatLogKind.MessageCreated when !string.IsNullOrEmpty(entry.MessageId):
                    if (!knownMessageIds.Contains(entry.MessageId) && !added.Contains(entry.MessageId))
                        added.Add(entry.MessageId);
                    break;

                case ChatLogKind.MessageDeleted when !string.IsNullOrEmpty(entry.MessageId):
                    if (!added.Remove(entry.MessageId) && knownMessageIds.Contains(entry.MessageId) && !deleted.Contains(entry.MessageId))
                        deleted.Add(entry.MessageId);
                    break;

                case ChatLogKind.Muted:
                    muted = true;
                    break;

                case ChatLogKind.Unmuted:
                    muted = false;
                    break;

                case ChatLogKind.Left:
                    left = true;
                    break;

                case ChatLogKind.ReactionChanged:
                    reactions = true;
                    break;

                case ChatLogKind.GroupChanged:
                    groupChanged = true;
                    break;
            }
        }

        return new ChatLogDelta(added, deleted, muted, left, reactions, inboxChanged, groupChanged);
    }

    /// <summary>The cursor to resume from after a log call: the one the server returned, else the one we had.</summary>
    public static string? NextLogCursor(string? current, string? returned) =>
        string.IsNullOrEmpty(returned) ? current : returned;
}

/// <summary>What kind of thing happened, reduced to what the app reacts to.</summary>
public enum ChatLogKind
{
    /// <summary>Reads, group events and anything this version doesn't act on.</summary>
    Other,
    MessageCreated,
    MessageDeleted,
    ReactionChanged,
    Muted,
    Unmuted,
    Left,
    /// <summary>A conversation started or was accepted.</summary>
    Began,
    /// <summary>Members, name, lock or join link of a group changed (its system message arrives as a message too).</summary>
    GroupChanged
}

/// <summary>One conversation log entry with the library types stripped off (built by ChatMapper.ToLogEntry).</summary>
public readonly record struct ChatLogEntry(ChatLogKind Kind, string ConversationId, string? MessageId = null);

/// <summary>The effect of a batch of log entries on one open conversation.</summary>
public sealed record ChatLogDelta(
    IReadOnlyList<string> NewMessageIds,
    IReadOnlyList<string> DeletedMessageIds,
    bool? Muted,
    bool LeftConversation,
    bool ReactionsChanged,
    bool InboxChanged,
    bool GroupChanged = false);

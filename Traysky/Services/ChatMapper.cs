using idunno.Bluesky.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using Traysky.ViewModels.Items;
using ChatProfile = idunno.Bluesky.Chat.Actor.ProfileViewBasic;

namespace Traysky.Services;

/// <summary>
/// Projects chat.bsky views into the plain item models the Messages pages bind to.
/// </summary>
public static class ChatMapper
{
    public static ActorItem ToActorItem(ChatProfile profile) => new()
    {
        Did = profile.Did.ToString(),
        Handle = profile.Handle?.ToString() ?? string.Empty,
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? string.Empty : profile.DisplayName.Trim(),
        AvatarUri = profile.Avatar
    };

    /// <summary>True when this member of a group holds the owner role.</summary>
    public static bool IsOwnerRole(ChatProfile member) =>
        member.Kind is idunno.Bluesky.Chat.Actor.GroupConversationMember { } groupMember
        && string.Equals(groupMember.Role, MemberRole.Owner, StringComparison.Ordinal);

    public static ConversationItem ToConversationItem(ConversationView view, string? selfDid)
    {
        Dictionary<string, ActorItem> members = new(StringComparer.Ordinal);
        foreach (ChatProfile member in view.Members ?? [])
            members[member.Did.ToString()] = ToActorItem(member);

        List<ActorItem> others = members.Values.Where(m => !string.Equals(m.Did, selfDid, StringComparison.Ordinal)).ToList();
        ActorItem? other = others.FirstOrDefault();

        string title;
        string subtitle;
        bool isGroup = view.Kind is GroupConversation;
        bool isOwner = false;
        bool isLocked = false;
        long memberCount = 0;
        long memberLimit = 0;
        if (view.Kind is GroupConversation group)
        {
            title = !string.IsNullOrWhiteSpace(group.Name)
                ? group.Name.Trim()
                : string.Join(", ", others.Select(o => o.NameOrHandle));
            memberCount = Convert.ToInt64(group.MemberCount);
            memberLimit = Convert.ToInt64(group.MemberLimit);
            subtitle = GroupChatPolicy.MemberCountText(memberCount, memberLimit);
            isLocked = string.Equals(group.LockStatus, ConversationLockStatus.Locked, StringComparison.Ordinal)
                || string.Equals(group.LockStatus, ConversationLockStatus.LockedPermanently, StringComparison.Ordinal);
            isOwner = view.Members?.Any(m => string.Equals(m.Did.ToString(), selfDid, StringComparison.Ordinal) && IsOwnerRole(m)) == true;
        }
        else
        {
            title = other?.NameOrHandle ?? "Conversation";
            subtitle = other?.HandleDisplay ?? string.Empty;
        }

        (string preview, DateTimeOffset? at) = Preview(view.LastMessage, selfDid, members, isGroup);

        return new ConversationItem
        {
            Id = view.Id,
            Title = title,
            Subtitle = subtitle,
            AvatarUri = isGroup ? null : other?.AvatarUri,
            OtherProfileKey = isGroup ? string.Empty : other?.ProfileKey ?? string.Empty,
            IsGroup = isGroup,
            IsOwner = isOwner,
            IsLocked = isLocked,
            MemberCount = memberCount,
            MemberLimit = memberLimit,
            IsMuted = view.Muted,
            Members = members,
            LastMessagePreview = preview,
            LastMessageAt = at,
            UnreadCount = view.UnreadCount,
            IsRequest = string.Equals(view.Status, ConversationStatus.Requested, StringComparison.Ordinal)
        };
    }

    /// <summary>
    /// A message in the thread, or null for anything shown nowhere (group system events,
    /// message kinds this version of the app doesn't know).
    /// </summary>
    public static MessageItem? ToMessageItem(MessageViewBase message, string? selfDid, IReadOnlyDictionary<string, ActorItem> members, bool isGroup)
    {
        switch (message)
        {
            case MessageView m:
            {
                string sender = m.Sender?.Did?.ToString() ?? string.Empty;
                members.TryGetValue(sender, out ActorItem? who);
                string text = m.Text ?? string.Empty;
                return new MessageItem
                {
                    Id = m.Id,
                    SenderDid = sender,
                    SenderName = who?.NameOrHandle ?? string.Empty,
                    SenderAvatarUri = who?.AvatarUri,
                    IsMine = string.Equals(sender, selfDid, StringComparison.Ordinal),
                    IsInGroup = isGroup,
                    Text = text,
                    Segments = FacetSegmenter.Segment(text, FeedMapper.ToFacetInputs(m.Facets)),
                    Embed = FeedMapper.ToEmbedItem(m.Embed),
                    Reactions = (m.Reactions ?? []).Where(r => !string.IsNullOrEmpty(r.Value))
                        .Select(r => (r.Sender?.Did?.ToString() ?? string.Empty, r.Value!)).ToList(),
                    ReactionsText = SummarizeReactions(m.Reactions?.Select(r => r.Value)),
                    SentAt = m.SentAt
                };
            }

            case DeletedMessageView d:
            {
                string sender = d.Sender?.Did?.ToString() ?? string.Empty;
                members.TryGetValue(sender, out ActorItem? who);
                return new MessageItem
                {
                    Id = d.Id,
                    SenderDid = sender,
                    SenderName = who?.NameOrHandle ?? string.Empty,
                    IsMine = string.Equals(sender, selfDid, StringComparison.Ordinal),
                    IsInGroup = isGroup,
                    IsDeleted = true,
                    SentAt = d.SentAt
                };
            }

            case SystemMessageView s when SystemText(s, selfDid, members) is { Length: > 0 } line:
                return new MessageItem
                {
                    Id = s.Id,
                    IsSystem = true,
                    IsInGroup = isGroup,
                    Text = line,
                    Segments = FacetSegmenter.Segment(line, null),
                    SentAt = s.SentAt
                };

            default:
                return null;
        }
    }

    /// <summary>The id of any message view, or null for a kind this version doesn't know.</summary>
    public static string? MessageIdOf(MessageViewBase? message) => message switch
    {
        MessageView m => m.Id,
        DeletedMessageView d => d.Id,
        SystemMessageView s => s.Id,
        _ => null
    };

    /// <summary>"Alice added Bob", for a group's system message; empty for a kind this version doesn't know.</summary>
    public static string SystemText(SystemMessageView message, string? selfDid, IReadOnlyDictionary<string, ActorItem> members)
    {
        string Name(object? who, bool mid)
        {
            string did = who switch
            {
                ChatProfile p => p.Did.ToString(),
                SystemMessageReferredUser r => r.Did.ToString(),
                _ => string.Empty
            };

            if (did.Length > 0 && string.Equals(did, selfDid, StringComparison.Ordinal))
                return mid ? "you" : "You";
            if (who is ChatProfile profile)
                return ToActorItem(profile).NameOrHandle;
            return members.TryGetValue(did, out ActorItem? known) ? known.NameOrHandle : string.Empty;
        }

        return message.Data switch
        {
            AddMemberSystemMessage a => GroupChatPolicy.EventText(GroupEvent.MemberAdded, Name(a.AddedBy, false), Name(a.Member, true)),
            RemoveMemberSystemMessage r => GroupChatPolicy.EventText(GroupEvent.MemberRemoved, Name(r.RemovedBy, false), Name(r.Member, true)),
            MemberJoinSystemMessage j => GroupChatPolicy.EventText(GroupEvent.MemberJoined, string.Empty, Name(j.Member, false)),
            MemberLeaveSystemMessage l => GroupChatPolicy.EventText(GroupEvent.MemberLeft, string.Empty, Name(l.Member, false)),
            EditGroupSystemMessage e => GroupChatPolicy.EventText(GroupEvent.Renamed, string.Empty, string.Empty, e.OldName ?? string.Empty, e.NewName ?? string.Empty),
            LockConversationSystemMessage k => GroupChatPolicy.EventText(GroupEvent.Locked, Name(k.LockedBy, false), string.Empty),
            LockConversationPermanentlySystemMessage k => GroupChatPolicy.EventText(GroupEvent.LockedPermanently, Name(k.LockedBy, false), string.Empty),
            UnlockConversationSystemMessage u => GroupChatPolicy.EventText(GroupEvent.Unlocked, Name(u.UnlockedBy, false), string.Empty),
            CreateJoinLinkSystemMessage => GroupChatPolicy.EventText(GroupEvent.LinkCreated, string.Empty, string.Empty),
            EnableJoinLinkSystemMessage => GroupChatPolicy.EventText(GroupEvent.LinkEnabled, string.Empty, string.Empty),
            DisableJoinLinkSystemMessage => GroupChatPolicy.EventText(GroupEvent.LinkDisabled, string.Empty, string.Empty),
            EditJoinLinkSystemMessage => GroupChatPolicy.EventText(GroupEvent.LinkEdited, string.Empty, string.Empty),
            _ => string.Empty
        };
    }

    /// <summary>Reduces a log entry to what <see cref="ChatThreadPolicy.Reduce"/> needs.</summary>
    public static ChatLogEntry ToLogEntry(LogBase log) => log switch
    {
        CreateMessage c => new ChatLogEntry(ChatLogKind.MessageCreated, c.ConversationId, MessageIdOf(c.Message)),
        DeleteMessage d => new ChatLogEntry(ChatLogKind.MessageDeleted, d.ConversationId, MessageIdOf(d.Message)),
        idunno.Bluesky.Chat.AddReaction r => new ChatLogEntry(ChatLogKind.ReactionChanged, r.ConversationId),
        idunno.Bluesky.Chat.RemoveReaction r => new ChatLogEntry(ChatLogKind.ReactionChanged, r.ConversationId),
        MuteConversation => new ChatLogEntry(ChatLogKind.Muted, log.ConversationId),
        UnmuteConversation => new ChatLogEntry(ChatLogKind.Unmuted, log.ConversationId),
        idunno.Bluesky.Chat.LeaveConversation => new ChatLogEntry(ChatLogKind.Left, log.ConversationId),
        BeginConversation or AcceptConversation => new ChatLogEntry(ChatLogKind.Began, log.ConversationId),
        idunno.Bluesky.Chat.AddMember or idunno.Bluesky.Chat.RemoveMember or idunno.Bluesky.Chat.MemberJoin or idunno.Bluesky.Chat.MemberLeave
            or idunno.Bluesky.Chat.LockConversation or idunno.Bluesky.Chat.LockConversationPermanently
            or idunno.Bluesky.Chat.CreateJoinLink or idunno.Bluesky.Chat.EnableJoinLink or idunno.Bluesky.Chat.DisableJoinLink or idunno.Bluesky.Chat.EditJoinLink
            or idunno.Bluesky.Chat.IncomingJoinRequest or idunno.Bluesky.Chat.ApproveJoinRequest or idunno.Bluesky.Chat.RejectJoinRequest
            => new ChatLogEntry(ChatLogKind.GroupChanged, log.ConversationId),
        _ => new ChatLogEntry(ChatLogKind.Other, log.ConversationId)
    };

    /// <summary>"❤️ 2  😂" - each distinct reaction once, with a count when more than one person used it.</summary>
    public static string SummarizeReactions(IEnumerable<string?>? values)
    {
        if (values is null)
            return string.Empty;

        return string.Join("  ", values
            .Where(v => !string.IsNullOrEmpty(v))
            .GroupBy(v => v, StringComparer.Ordinal)
            .Select(g => g.Count() > 1 ? $"{g.Key} {g.Count()}" : g.Key));
    }

    private static (string Preview, DateTimeOffset? At) Preview(MessageViewBase? last, string? selfDid, IReadOnlyDictionary<string, ActorItem> members, bool isGroup)
    {
        switch (last)
        {
            case MessageView m:
            {
                string sender = m.Sender?.Did?.ToString() ?? string.Empty;
                string body = !string.IsNullOrWhiteSpace(m.Text) ? m.Text.ReplaceLineEndings(" ").Trim()
                    : m.Embed is not null ? "Shared a post"
                    : string.Empty;
                return (PrefixSender(body, sender, selfDid, members, isGroup), m.SentAt);
            }

            case DeletedMessageView d:
                return ("Message deleted", d.SentAt);

            case SystemMessageView s:
                return (SystemText(s, selfDid, members), s.SentAt);

            default:
                return (string.Empty, null);
        }
    }

    private static string PrefixSender(string body, string sender, string? selfDid, IReadOnlyDictionary<string, ActorItem> members, bool isGroup)
    {
        if (string.Equals(sender, selfDid, StringComparison.Ordinal))
            return "You: " + body;

        if (isGroup && members.TryGetValue(sender, out ActorItem? who))
            return who.NameOrHandle + ": " + body;

        return body;
    }
}

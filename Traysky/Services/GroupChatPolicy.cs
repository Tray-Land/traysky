using System;
using System.Globalization;
using System.Text;

namespace Traysky.Services;

/// <summary>Things that happen inside a group conversation and show as a line in the thread.</summary>
public enum GroupEvent
{
    MemberAdded,
    MemberRemoved,
    MemberJoined,
    MemberLeft,
    Renamed,
    LinkCreated,
    LinkEnabled,
    LinkDisabled,
    LinkEdited,
    Locked,
    LockedPermanently,
    Unlocked
}

/// <summary>What the signed-in account may do in one group.</summary>
public readonly record struct GroupPermissions(bool CanRename, bool CanAddMembers, bool CanRemoveMembers, bool CanManageLink, bool CanSend);

/// <summary>
/// Decisions for group chats: who may do what, whether a new group can be created, the text of
/// the system lines in a thread, and pulling a join code out of whatever was pasted. No WinRT
/// dependency, so it is linked into the tests. The size limits live in the library
/// (<c>Maximum.GroupNameLengthInGraphemes</c> and friends), so callers pass them in.
/// </summary>
public static class GroupChatPolicy
{
    private const int MaxJoinCodeLength = 128;

    /// <summary>The name trimmed, or null when it is empty or over either limit.</summary>
    public static string? NormalizeName(string? name, int maxGraphemes, int maxBytes)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return null;

        if (new StringInfo(trimmed).LengthInTextElements > maxGraphemes || Encoding.UTF8.GetByteCount(trimmed) > maxBytes)
            return null;

        return trimmed;
    }

    /// <summary>
    /// A group needs a valid name and at least one other person; with the owner added, the
    /// total must fit under <paramref name="memberLimit"/>.
    /// </summary>
    public static bool CanCreate(string? name, int otherMembers, int maxNameGraphemes, int maxNameBytes, int memberLimit) =>
        NormalizeName(name, maxNameGraphemes, maxNameBytes) is not null
        && otherMembers >= 1
        && otherMembers + 1 <= memberLimit;

    /// <summary>How many more people can still be picked for a new group or added to an existing one.</summary>
    public static int RoomLeft(int currentMembers, int memberLimit) => Math.Max(0, memberLimit - currentMembers);

    /// <summary>Only the owner manages a group; a locked group takes no new messages.</summary>
    public static GroupPermissions PermissionsFor(bool isOwner, bool isLocked) =>
        new(isOwner, isOwner && !isLocked, isOwner, isOwner, !isLocked);

    /// <summary>"12 members", or "12 of 100 members" once the group is close to full.</summary>
    public static string MemberCountText(long count, long limit)
    {
        string noun = count == 1 ? "member" : "members";
        return limit > 0 && count >= limit - 5 ? $"{count} of {limit} members" : $"{count} {noun}";
    }

    /// <summary>
    /// The join code from a pasted link (the last path segment of any address, including a
    /// <c>traysky:join/CODE</c> link) or from the bare code. Null when nothing usable is there.
    /// </summary>
    public static string? ParseJoinCode(string? input)
    {
        string text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return null;

        string candidate = text;
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
        {
            string[] segments = (uri.Host.Length > 0 && uri.Scheme == "traysky" ? uri.Host + uri.AbsolutePath : uri.AbsolutePath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            // "traysky:join" alone, or an address that ends at the "join" word, has no code in it.
            if (segments.Length == 0 || string.Equals(segments[^1], "join", StringComparison.OrdinalIgnoreCase))
                return null;

            candidate = Uri.UnescapeDataString(segments[^1]);
        }
        else if (text.Contains('/'))
        {
            candidate = text.TrimEnd('/');
            candidate = candidate[(candidate.LastIndexOf('/') + 1)..];
        }

        return IsCode(candidate) ? candidate : null;
    }

    private static bool IsCode(string candidate)
    {
        if (candidate.Length == 0 || candidate.Length > MaxJoinCodeLength)
            return false;

        foreach (char c in candidate)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The line a system message shows. <paramref name="actor"/> is who did it and <paramref name="target"/>
    /// who it was done to, either of which may be empty when the server didn't say.
    /// </summary>
    public static string EventText(GroupEvent kind, string actor, string target, string oldName = "", string newName = "")
    {
        bool hasActor = actor.Length > 0;
        bool hasTarget = target.Length > 0;
        string who = hasTarget ? target : "Someone";

        return Capitalize(kind switch
        {
            GroupEvent.MemberAdded => hasActor ? $"{actor} added {who}" : $"{who} was added to the group",
            GroupEvent.MemberRemoved => hasActor ? $"{actor} removed {who}" : $"{who} was removed from the group",
            GroupEvent.MemberJoined => $"{who} joined the group",
            GroupEvent.MemberLeft => $"{who} left the group",
            GroupEvent.Renamed when newName.Length > 0 => hasActor ? $"{actor} renamed the group to “{newName}”" : $"The group was renamed to “{newName}”",
            GroupEvent.Renamed => hasActor ? $"{actor} renamed the group" : "The group was renamed",
            GroupEvent.LinkCreated => "The join link was created",
            GroupEvent.LinkEnabled => "The join link was turned on",
            GroupEvent.LinkDisabled => "The join link was turned off",
            GroupEvent.LinkEdited => "The join link settings were changed",
            GroupEvent.Locked => hasActor ? $"{actor} locked the group" : "The group was locked",
            GroupEvent.LockedPermanently => hasActor ? $"{actor} locked the group for good" : "The group was locked for good",
            GroupEvent.Unlocked => hasActor ? $"{actor} unlocked the group" : "The group was unlocked",
            _ => string.Empty
        });
    }

    /// <summary>"you" names the signed-in account mid-sentence, so a line that starts with it needs its capital.</summary>
    private static string Capitalize(string text) =>
        text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    /// <summary>One line describing a join link, shown under its switch.</summary>
    public static string JoinLinkSummary(bool enabled, bool requireApproval, bool followedByOwnerOnly)
    {
        if (!enabled)
            return "The link is off. Nobody can join with it.";

        string who = followedByOwnerOnly ? "People you follow can join" : "Anyone with the link can join";
        return requireApproval ? who + ", once you approve them." : who + " right away.";
    }

    /// <summary>What the preview of a link someone pasted says about joining.</summary>
    public static string JoinButtonText(bool requireApproval) => requireApproval ? "Request to join" : "Join group";

    /// <summary>The line shown after a join request went out.</summary>
    public static string JoinOutcomeText(bool joined, string groupName) => joined
        ? $"You joined {groupName}."
        : $"Your request to join {groupName} was sent. The owner has to approve it.";
}

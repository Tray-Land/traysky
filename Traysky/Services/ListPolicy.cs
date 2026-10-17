using System;
using System.Globalization;
using System.Text;

namespace Traysky.Services;

/// <summary>What a list is for. Reference lists back starter packs and are not managed here.</summary>
public enum ListKind
{
    Curate,
    Moderation,
    Reference,
    Unknown
}

/// <summary>What toggling a checkbox in "Add to list" has to do on the server.</summary>
public enum MembershipChange
{
    None,
    Add,
    Remove
}

/// <summary>Decisions for list management. Pure .NET on purpose.</summary>
public static class ListPolicy
{
    /// <summary>The lexicon caps a name at 64 UTF-8 bytes (and idunno enforces it), not 64 characters.</summary>
    public const int NameMaxBytes = 64;
    public const int DescriptionMaxGraphemes = 300;

    /// <summary>Curation and moderation lists are the ones people make and browse; reference lists belong to starter packs.</summary>
    public static bool IsShown(ListKind kind) => kind is ListKind.Curate or ListKind.Moderation;

    /// <summary>Only the list's creator can edit it, delete it or change its members.</summary>
    public static bool CanManage(string? creatorDid, string? sessionDid) =>
        !string.IsNullOrEmpty(creatorDid) && string.Equals(creatorDid, sessionDid, StringComparison.Ordinal);

    public static string KindLabel(ListKind kind) => kind switch
    {
        ListKind.Moderation => "Moderation list",
        ListKind.Reference => "Starter pack list",
        ListKind.Curate => "Curation list",
        _ => "List"
    };

    public static string KindHint(ListKind kind) => kind switch
    {
        ListKind.Moderation => "Mute or block everyone on it in one go.",
        _ => "Follow everyone on it as a feed of their posts."
    };

    public static string EmptyText(bool isSelf) => isSelf ? "You haven't made any lists" : "No lists";

    public static string EmptyMembersText(bool canManage) =>
        canManage ? "No one is on this list yet. Search above to add people." : "No one is on this list.";

    public static string MemberCountText(int count) => count == 1 ? "1 account" : $"{count:N0} accounts";

    /// <summary>The line under a list's name: its kind, then its size.</summary>
    public static string Subtitle(ListKind kind, int memberCount) => $"{KindLabel(kind)} · {MemberCountText(memberCount)}";

    /// <summary>Trims the name; null when it is empty or too long to save.</summary>
    public static string? NormalizeName(string? name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length > 0 && Encoding.UTF8.GetByteCount(trimmed) <= NameMaxBytes ? trimmed : null;
    }

    public static bool IsDescriptionValid(string? description) =>
        GraphemeCount(description) <= DescriptionMaxGraphemes;

    /// <summary>Whether the dialog's Save button should be on.</summary>
    public static bool CanSave(string? name, string? description) =>
        NormalizeName(name) is not null && IsDescriptionValid(description);

    /// <summary>Only moderation lists can be muted or blocked as a whole, whoever made them.</summary>
    public static bool CanSubscribe(ListKind kind) => kind == ListKind.Moderation;

    public static string MuteLabel(bool muted) => muted ? "Unmute list" : "Mute list";

    public static string BlockLabel(bool blocked) => blocked ? "Unblock list" : "Block list";

    /// <summary>The line under the buttons saying what the signed-in account already does with the list; empty when nothing.</summary>
    public static string SubscriptionStatus(bool muted, bool blocked) => (muted, blocked) switch
    {
        (true, true) => "You mute and block everyone on this list.",
        (true, false) => "You mute everyone on this list.",
        (false, true) => "You block everyone on this list.",
        _ => string.Empty
    };

    public static string BlockConfirmText(string listName) =>
        $"Everyone on {listName} is blocked: they can't see your posts and you won't see theirs. People added later are blocked too. You can unblock the list at any time.";

    public static string SubscriptionFailureText(bool block, bool turningOn) => (block, turningOn) switch
    {
        (false, true) => "Couldn't mute the list. Try again.",
        (false, false) => "Couldn't unmute the list. Try again.",
        (true, true) => "Couldn't block the list. Try again.",
        (true, false) => "Couldn't unblock the list. Try again."
    };

    public static MembershipChange Change(bool wasMember, bool isMember) =>
        wasMember == isMember ? MembershipChange.None : isMember ? MembershipChange.Add : MembershipChange.Remove;

    public static string MembershipFailureText(MembershipChange change, string listName) => change switch
    {
        MembershipChange.Add => $"Couldn't add them to {listName}. Try again.",
        MembershipChange.Remove => $"Couldn't remove them from {listName}. Try again.",
        _ => string.Empty
    };

    private static int GraphemeCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        StringInfo info = new(text.Trim());
        return info.LengthInTextElements;
    }
}

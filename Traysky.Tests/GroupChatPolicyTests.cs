using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class GroupChatPolicyTests
{
    private const int Graphemes = 128;
    private const int Bytes = 1280;

    [TestMethod]
    public void NameIsTrimmedAndMustNotBeEmpty()
    {
        Assert.AreEqual("Tray club", GroupChatPolicy.NormalizeName("  Tray club  ", Graphemes, Bytes));
        Assert.IsNull(GroupChatPolicy.NormalizeName("   ", Graphemes, Bytes));
        Assert.IsNull(GroupChatPolicy.NormalizeName(null, Graphemes, Bytes));
    }

    [TestMethod]
    public void NameLimitsCountGraphemesAndBytes()
    {
        Assert.IsNotNull(GroupChatPolicy.NormalizeName(new string('a', 10), 10, 10));
        Assert.IsNull(GroupChatPolicy.NormalizeName(new string('a', 11), 10, 100));

        // Three flags are three graphemes but far more than three bytes.
        string flags = "🇬🇧🇫🇷🇩🇪";
        Assert.IsNotNull(GroupChatPolicy.NormalizeName(flags, 3, 100));
        Assert.IsNull(GroupChatPolicy.NormalizeName(flags, 3, 20));
    }

    [TestMethod]
    public void CreatingNeedsANameAndAtLeastOneOtherPerson()
    {
        Assert.IsTrue(GroupChatPolicy.CanCreate("Club", 1, Graphemes, Bytes, 100));
        Assert.IsFalse(GroupChatPolicy.CanCreate("Club", 0, Graphemes, Bytes, 100));
        Assert.IsFalse(GroupChatPolicy.CanCreate("  ", 3, Graphemes, Bytes, 100));
    }

    [TestMethod]
    public void TheOwnerCountsTowardTheMemberLimit()
    {
        Assert.IsTrue(GroupChatPolicy.CanCreate("Club", 99, Graphemes, Bytes, 100));
        Assert.IsFalse(GroupChatPolicy.CanCreate("Club", 100, Graphemes, Bytes, 100));
        Assert.AreEqual(5, GroupChatPolicy.RoomLeft(95, 100));
        Assert.AreEqual(0, GroupChatPolicy.RoomLeft(120, 100));
    }

    [TestMethod]
    public void OnlyTheOwnerManagesAGroup()
    {
        GroupPermissions member = GroupChatPolicy.PermissionsFor(isOwner: false, isLocked: false);
        Assert.IsFalse(member.CanRename || member.CanAddMembers || member.CanRemoveMembers || member.CanManageLink);
        Assert.IsTrue(member.CanSend);

        GroupPermissions owner = GroupChatPolicy.PermissionsFor(isOwner: true, isLocked: false);
        Assert.IsTrue(owner.CanRename && owner.CanAddMembers && owner.CanRemoveMembers && owner.CanManageLink && owner.CanSend);
    }

    [TestMethod]
    public void ALockedGroupTakesNoMessagesOrNewPeople()
    {
        GroupPermissions owner = GroupChatPolicy.PermissionsFor(isOwner: true, isLocked: true);
        Assert.IsFalse(owner.CanSend);
        Assert.IsFalse(owner.CanAddMembers);
        Assert.IsTrue(owner.CanRemoveMembers);
        Assert.IsFalse(GroupChatPolicy.PermissionsFor(isOwner: false, isLocked: true).CanSend);
    }

    [TestMethod]
    public void MemberCountTextWarnsNearTheLimit()
    {
        Assert.AreEqual("1 member", GroupChatPolicy.MemberCountText(1, 100));
        Assert.AreEqual("12 members", GroupChatPolicy.MemberCountText(12, 100));
        Assert.AreEqual("96 of 100 members", GroupChatPolicy.MemberCountText(96, 100));
        Assert.AreEqual("12 members", GroupChatPolicy.MemberCountText(12, 0));
    }

    [TestMethod]
    [DataRow("AbC123", "AbC123")]
    [DataRow("  AbC123  ", "AbC123")]
    [DataRow("https://bsky.app/messages/join/AbC123", "AbC123")]
    [DataRow("https://bsky.app/messages/join/AbC123/", "AbC123")]
    [DataRow("https://bsky.app/messages/join/AbC123?x=1", "AbC123")]
    [DataRow("traysky:join/AbC123", "AbC123")]
    [DataRow("traysky://join/AbC123", "AbC123")]
    [DataRow("bsky.app/messages/join/AbC123", "AbC123")]
    [DataRow("a-b_c", "a-b_c")]
    public void JoinCodeComesOutOfALinkOrBareCode(string input, string expected)
    {
        Assert.AreEqual(expected, GroupChatPolicy.ParseJoinCode(input));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("two words")]
    [DataRow("traysky:join")]
    [DataRow("traysky://join")]
    [DataRow("https://bsky.app/messages/join")]
    [DataRow("https://bsky.app/")]
    [DataRow("bad!code")]
    public void NothingUsableGivesNoCode(string input)
    {
        Assert.IsNull(GroupChatPolicy.ParseJoinCode(input));
    }

    [TestMethod]
    public void NullGivesNoCode()
    {
        Assert.IsNull(GroupChatPolicy.ParseJoinCode(null));
    }

    [TestMethod]
    public void SystemLinesNameWhoDidWhat()
    {
        Assert.AreEqual("Alice added Bob", GroupChatPolicy.EventText(GroupEvent.MemberAdded, "Alice", "Bob"));
        Assert.AreEqual("You added Bob", GroupChatPolicy.EventText(GroupEvent.MemberAdded, "You", "Bob"));
        Assert.AreEqual("Alice added you", GroupChatPolicy.EventText(GroupEvent.MemberAdded, "Alice", "you"));
        Assert.AreEqual("Alice removed Bob", GroupChatPolicy.EventText(GroupEvent.MemberRemoved, "Alice", "Bob"));
        Assert.AreEqual("Bob joined the group", GroupChatPolicy.EventText(GroupEvent.MemberJoined, string.Empty, "Bob"));
        Assert.AreEqual("Bob left the group", GroupChatPolicy.EventText(GroupEvent.MemberLeft, string.Empty, "Bob"));
    }

    [TestMethod]
    public void SystemLinesStillReadWhenTheServerLeavesNamesOut()
    {
        Assert.AreEqual("Someone was added to the group", GroupChatPolicy.EventText(GroupEvent.MemberAdded, string.Empty, string.Empty));
        Assert.AreEqual("Someone joined the group", GroupChatPolicy.EventText(GroupEvent.MemberJoined, string.Empty, string.Empty));
        Assert.AreEqual("The group was locked", GroupChatPolicy.EventText(GroupEvent.Locked, string.Empty, string.Empty));
    }

    [TestMethod]
    public void ALineStartingWithYouIsCapitalised()
    {
        Assert.AreEqual("You joined the group", GroupChatPolicy.EventText(GroupEvent.MemberJoined, string.Empty, "you"));
        Assert.AreEqual("You left the group", GroupChatPolicy.EventText(GroupEvent.MemberLeft, string.Empty, "you"));
    }

    [TestMethod]
    public void RenameLineQuotesTheNewName()
    {
        Assert.AreEqual("The group was renamed to “Tray club”", GroupChatPolicy.EventText(GroupEvent.Renamed, string.Empty, string.Empty, "Old", "Tray club"));
        Assert.AreEqual("Alice renamed the group to “Tray club”", GroupChatPolicy.EventText(GroupEvent.Renamed, "Alice", string.Empty, "Old", "Tray club"));
        Assert.AreEqual("The group was renamed", GroupChatPolicy.EventText(GroupEvent.Renamed, string.Empty, string.Empty));
    }

    [TestMethod]
    public void JoinLinkSummaryDescribesWhoCanUseIt()
    {
        StringAssert.Contains(GroupChatPolicy.JoinLinkSummary(false, true, false), "off");
        StringAssert.Contains(GroupChatPolicy.JoinLinkSummary(true, false, false), "Anyone");
        StringAssert.Contains(GroupChatPolicy.JoinLinkSummary(true, false, false), "right away");
        StringAssert.Contains(GroupChatPolicy.JoinLinkSummary(true, true, true), "People you follow");
        StringAssert.Contains(GroupChatPolicy.JoinLinkSummary(true, true, true), "approve");
    }

    [TestMethod]
    public void JoinButtonAndOutcomeFollowApproval()
    {
        Assert.AreEqual("Request to join", GroupChatPolicy.JoinButtonText(true));
        Assert.AreEqual("Join group", GroupChatPolicy.JoinButtonText(false));
        StringAssert.Contains(GroupChatPolicy.JoinOutcomeText(true, "Club"), "joined");
        StringAssert.Contains(GroupChatPolicy.JoinOutcomeText(false, "Club"), "approve");
    }
}

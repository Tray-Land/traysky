using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ChatPolicyTests
{
    [TestMethod]
    public void SuccessfulProbeAllowsChat()
    {
        Assert.AreEqual(ChatAccess.Allowed, ChatAccessPolicy.FromProbe(true, 200, null));
    }

    [TestMethod]
    public void ScopeAndSupportFailuresDenyChat()
    {
        // App password created without DM access.
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 400, "InvalidToken"));
        // OAuth grant without the chat scope.
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 401, "InvalidToken"));
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 403, null));
        // PDS that doesn't proxy chat.
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 404, "XRPCNotSupported"));
        Assert.AreEqual(ChatAccess.Denied, ChatAccessPolicy.FromProbe(false, 501, null));
    }

    [TestMethod]
    public void TransientFailuresSayNothing()
    {
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 0, null));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 408, null));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 429, "RateLimitExceeded"));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 500, null));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 502, null));
        Assert.AreEqual(ChatAccess.Unknown, ChatAccessPolicy.FromProbe(false, 400, "ExpiredToken"));
    }

    [TestMethod]
    public void MessageButtonFollowsTheirSetting()
    {
        Assert.IsFalse(ChatAccessPolicy.CanOfferMessage(isSelf: true, IncomingChatSetting.All, theyFollowYou: true));

        Assert.IsTrue(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.All, false));
        Assert.IsFalse(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.None, true));
        Assert.IsTrue(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.Following, true));
        Assert.IsFalse(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.Following, false));

        // No declaration means "people I follow".
        Assert.IsTrue(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.NotDeclared, true));
        Assert.IsFalse(ChatAccessPolicy.CanOfferMessage(false, IncomingChatSetting.NotDeclared, false));
    }

    [TestMethod]
    public void RunsBreakOnSenderOrPause()
    {
        DateTimeOffset t = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

        Assert.IsTrue(ChatThreadPolicy.StartsRun(null, null, "did:a", t));
        Assert.IsFalse(ChatThreadPolicy.StartsRun("did:a", t, "did:a", t.AddMinutes(1)));
        Assert.IsFalse(ChatThreadPolicy.StartsRun("did:a", t, "did:a", t + ChatThreadPolicy.RunGap));
        Assert.IsTrue(ChatThreadPolicy.StartsRun("did:a", t, "did:a", t + ChatThreadPolicy.RunGap + TimeSpan.FromSeconds(1)));
        Assert.IsTrue(ChatThreadPolicy.StartsRun("did:a", t, "did:b", t.AddSeconds(5)));
    }

    [TestMethod]
    public void SendNeedsSomeTextWithinTheLimit()
    {
        Assert.IsFalse(ChatThreadPolicy.CanSend(null));
        Assert.IsFalse(ChatThreadPolicy.CanSend("   \r\n "));
        Assert.IsTrue(ChatThreadPolicy.CanSend("hi"));
        Assert.IsTrue(ChatThreadPolicy.CanSend(new string('x', ChatThreadPolicy.MaxMessageLength)));
        Assert.IsFalse(ChatThreadPolicy.CanSend(new string('x', ChatThreadPolicy.MaxMessageLength + 1)));
    }

    private static ChatLogEntry Entry(ChatLogKind kind, string convo = "c1", string? message = null) => new(kind, convo, message);

    [TestMethod]
    public void NewMessagesInThisConversationAreAddedOnceInOrder()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce(
            [Entry(ChatLogKind.MessageCreated, "c1", "m2"), Entry(ChatLogKind.MessageCreated, "c1", "m3"), Entry(ChatLogKind.MessageCreated, "c1", "m2")],
            "c1", new HashSet<string> { "m1" });

        CollectionAssert.AreEqual(new[] { "m2", "m3" }, delta.NewMessageIds.ToArray());
        Assert.IsTrue(delta.InboxChanged);
    }

    [TestMethod]
    public void MessagesAlreadyShownAreNotAddedAgain()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce([Entry(ChatLogKind.MessageCreated, "c1", "m1")], "c1", new HashSet<string> { "m1" });

        Assert.AreEqual(0, delta.NewMessageIds.Count);
    }

    [TestMethod]
    public void OtherConversationsOnlyChangeTheInbox()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce(
            [Entry(ChatLogKind.MessageCreated, "c2", "x1"), Entry(ChatLogKind.Muted, "c2"), Entry(ChatLogKind.Left, "c2")],
            "c1", new HashSet<string>());

        Assert.AreEqual(0, delta.NewMessageIds.Count);
        Assert.IsNull(delta.Muted);
        Assert.IsFalse(delta.LeftConversation);
        Assert.IsTrue(delta.InboxChanged);
    }

    [TestMethod]
    public void ReadsAndReactionsDoNotChangeTheInbox()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce([Entry(ChatLogKind.Other, "c2"), Entry(ChatLogKind.ReactionChanged, "c2")], "c1", new HashSet<string>());

        Assert.IsFalse(delta.InboxChanged);
        Assert.IsFalse(delta.ReactionsChanged);
    }

    [TestMethod]
    public void DeletedMessagesAreRemovedOnlyIfShown()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce(
            [Entry(ChatLogKind.MessageDeleted, "c1", "m1"), Entry(ChatLogKind.MessageDeleted, "c1", "m9")],
            "c1", new HashSet<string> { "m1" });

        CollectionAssert.AreEqual(new[] { "m1" }, delta.DeletedMessageIds.ToArray());
    }

    [TestMethod]
    public void MessageCreatedAndDeletedInOneBatchIsNeverAdded()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce(
            [Entry(ChatLogKind.MessageCreated, "c1", "m5"), Entry(ChatLogKind.MessageDeleted, "c1", "m5")],
            "c1", new HashSet<string>());

        Assert.AreEqual(0, delta.NewMessageIds.Count);
        Assert.AreEqual(0, delta.DeletedMessageIds.Count);
    }

    [TestMethod]
    public void LastMuteEntryWins()
    {
        Assert.AreEqual(false, ChatThreadPolicy.Reduce([Entry(ChatLogKind.Muted), Entry(ChatLogKind.Unmuted)], "c1", new HashSet<string>()).Muted);
        Assert.AreEqual(true, ChatThreadPolicy.Reduce([Entry(ChatLogKind.Unmuted), Entry(ChatLogKind.Muted)], "c1", new HashSet<string>()).Muted);
        Assert.IsNull(ChatThreadPolicy.Reduce([Entry(ChatLogKind.Other)], "c1", new HashSet<string>()).Muted);
    }

    [TestMethod]
    public void LeavingAndReactionsInThisConversationAreReported()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce([Entry(ChatLogKind.Left), Entry(ChatLogKind.ReactionChanged)], "c1", new HashSet<string>());

        Assert.IsTrue(delta.LeftConversation);
        Assert.IsTrue(delta.ReactionsChanged);
    }

    [TestMethod]
    public void LogCursorKeepsTheOldOneWhenNoneComesBack()
    {
        Assert.AreEqual("b", ChatThreadPolicy.NextLogCursor("a", "b"));
        Assert.AreEqual("a", ChatThreadPolicy.NextLogCursor("a", null));
        Assert.AreEqual("a", ChatThreadPolicy.NextLogCursor("a", ""));
        Assert.IsNull(ChatThreadPolicy.NextLogCursor(null, null));
    }

    [TestMethod]
    public void RequestsAreAcceptedOrDeclinedNotMuted()
    {
        ChatConversationMenu request = ChatConversationMenuPolicy.For(isRequest: true, isMuted: false, isGroup: false);
        Assert.IsTrue(request.ShowAccept);
        Assert.IsFalse(request.ShowMute);
        Assert.IsFalse(request.ShowUnmute);
        Assert.AreEqual("Decline request", request.LeaveLabel);
    }

    [TestMethod]
    public void MuteMenuFlipsWithTheMutedState()
    {
        ChatConversationMenu open = ChatConversationMenuPolicy.For(false, isMuted: false, isGroup: false);
        Assert.IsTrue(open.ShowMute);
        Assert.IsFalse(open.ShowUnmute);
        Assert.IsFalse(open.ShowAccept);
        Assert.AreEqual("Leave conversation", open.LeaveLabel);

        ChatConversationMenu muted = ChatConversationMenuPolicy.For(false, isMuted: true, isGroup: true);
        Assert.IsFalse(muted.ShowMute);
        Assert.IsTrue(muted.ShowUnmute);
        Assert.AreEqual("Leave group", muted.LeaveLabel);
    }

    [TestMethod]
    public void BadgeCountNeverGoesNegative()
    {
        Assert.AreEqual(0, ChatConversationMenuPolicy.BadgeCount(-1));
        Assert.AreEqual(3, ChatConversationMenuPolicy.BadgeCount(3));
    }

    [TestMethod]
    public void GroupsOfferGroupInfoButDirectChatsAndRequestsDoNot()
    {
        Assert.IsTrue(ChatConversationMenuPolicy.For(isRequest: false, isMuted: false, isGroup: true).ShowGroupInfo);
        Assert.IsFalse(ChatConversationMenuPolicy.For(isRequest: false, isMuted: false, isGroup: false).ShowGroupInfo);
        Assert.IsFalse(ChatConversationMenuPolicy.For(isRequest: true, isMuted: false, isGroup: true).ShowGroupInfo);
    }

    [TestMethod]
    public void GroupChangesInTheOpenGroupAreReportedAndChangeTheInbox()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce([Entry(ChatLogKind.GroupChanged, "c1")], "c1", new HashSet<string>());

        Assert.IsTrue(delta.GroupChanged);
        Assert.IsTrue(delta.InboxChanged);
    }

    [TestMethod]
    public void GroupChangesElsewhereOnlyChangeTheInbox()
    {
        ChatLogDelta delta = ChatThreadPolicy.Reduce([Entry(ChatLogKind.GroupChanged, "c2")], "c1", new HashSet<string>());

        Assert.IsFalse(delta.GroupChanged);
        Assert.IsTrue(delta.InboxChanged);
    }
}

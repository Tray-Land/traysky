using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ActivitySubscriptionPolicyTests
{
    [TestMethod]
    public void FromFlags_RepliesNeedPosts()
    {
        Assert.AreEqual(ActivitySubscriptionLevel.Off, ActivitySubscriptionPolicy.FromFlags(false, false));
        Assert.AreEqual(ActivitySubscriptionLevel.Off, ActivitySubscriptionPolicy.FromFlags(false, true));
        Assert.AreEqual(ActivitySubscriptionLevel.Posts, ActivitySubscriptionPolicy.FromFlags(true, false));
        Assert.AreEqual(ActivitySubscriptionLevel.PostsAndReplies, ActivitySubscriptionPolicy.FromFlags(true, true));
    }

    [TestMethod]
    public void ToFlags_RoundTripsAndNeverSendsRepliesWithoutPosts()
    {
        foreach (ActivitySubscriptionLevel level in new[] { ActivitySubscriptionLevel.Off, ActivitySubscriptionLevel.Posts, ActivitySubscriptionLevel.PostsAndReplies })
        {
            (bool posts, bool replies) = ActivitySubscriptionPolicy.ToFlags(level);

            Assert.AreEqual(level, ActivitySubscriptionPolicy.FromFlags(posts, replies));
            Assert.IsFalse(replies && !posts);
        }
    }

    [TestMethod]
    public void Labels_NameEachLevel()
    {
        Assert.AreEqual("Off", ActivitySubscriptionPolicy.Label(ActivitySubscriptionLevel.Off));
        Assert.AreEqual("Posts", ActivitySubscriptionPolicy.Label(ActivitySubscriptionLevel.Posts));
        Assert.AreEqual("Posts and replies", ActivitySubscriptionPolicy.Label(ActivitySubscriptionLevel.PostsAndReplies));
        Assert.AreEqual("Get notified about their posts", ActivitySubscriptionPolicy.BellTooltip(ActivitySubscriptionLevel.Off));
        Assert.AreEqual("Notifications on: posts and replies", ActivitySubscriptionPolicy.BellTooltip(ActivitySubscriptionLevel.PostsAndReplies));
    }

    [TestMethod]
    public void CanSubscribe_FollowersIsTheDefault()
    {
        Assert.IsTrue(ActivitySubscriptionPolicy.CanSubscribe(null, isSelf: false, following: true, followedBy: false));
        Assert.IsTrue(ActivitySubscriptionPolicy.CanSubscribe("followers", isSelf: false, following: true, followedBy: false));
        Assert.IsFalse(ActivitySubscriptionPolicy.CanSubscribe(null, isSelf: false, following: false, followedBy: true));
    }

    [TestMethod]
    public void CanSubscribe_MutualsNeedsBothDirections()
    {
        Assert.IsTrue(ActivitySubscriptionPolicy.CanSubscribe("mutuals", false, true, true));
        Assert.IsFalse(ActivitySubscriptionPolicy.CanSubscribe("mutuals", false, true, false));
        Assert.IsFalse(ActivitySubscriptionPolicy.CanSubscribe("mutuals", false, false, true));
    }

    [TestMethod]
    public void CanSubscribe_NoneUnknownAndSelfAreClosed()
    {
        Assert.IsFalse(ActivitySubscriptionPolicy.CanSubscribe("none", false, true, true));
        Assert.IsFalse(ActivitySubscriptionPolicy.CanSubscribe("someday", false, true, true));
        Assert.IsFalse(ActivitySubscriptionPolicy.CanSubscribe("followers", isSelf: true, following: true, followedBy: true));
    }

    [TestMethod]
    public void ShowBell_StaysWhenAlreadySubscribedSoItCanBeTurnedOff()
    {
        Assert.IsFalse(ActivitySubscriptionPolicy.ShowBell("none", false, true, true, ActivitySubscriptionLevel.Off));
        Assert.IsTrue(ActivitySubscriptionPolicy.ShowBell("none", false, true, true, ActivitySubscriptionLevel.Posts));
        Assert.IsTrue(ActivitySubscriptionPolicy.ShowBell("followers", false, true, false, ActivitySubscriptionLevel.Off));
        Assert.IsFalse(ActivitySubscriptionPolicy.ShowBell("followers", isSelf: true, following: true, followedBy: true, ActivitySubscriptionLevel.Posts));
    }

    [TestMethod]
    public void ToastVerb_SubscribedPostsReadAsPosted()
    {
        Assert.AreEqual("posted", NotificationGroupingPolicy.ToastVerb(NotificationKind.SubscribedPost));
        Assert.AreEqual("replied", NotificationGroupingPolicy.ToastVerb(NotificationKind.Reply));
        Assert.AreEqual("quoted you", NotificationGroupingPolicy.ToastVerb(NotificationKind.Quote));
        Assert.AreEqual("mentioned you", NotificationGroupingPolicy.ToastVerb(NotificationKind.Mention));
    }

    [TestMethod]
    public void SubscribedPosts_AreNeverGroupedAndKeepTheirOwnHeadline()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        NotificationInput[] inputs =
        [
            new("1", NotificationKind.SubscribedPost, "Alice", null, false, now),
            new("2", NotificationKind.SubscribedPost, "Alice", null, false, now.AddMinutes(-1))
        ];

        IReadOnlyList<NotificationGroup> groups = NotificationGroupingPolicy.Group(inputs);

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual("Alice posted", NotificationGroupingPolicy.Headline(groups[0]));
    }
}

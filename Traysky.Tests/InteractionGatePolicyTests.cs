using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class InteractionGatePolicyTests
{
    [TestMethod]
    public void ToRules_EveryoneIsNoGate_NobodyIsEmpty()
    {
        Assert.IsNull(InteractionGatePolicy.ToRules(ReplyChoice.Everyone));

        var nobody = InteractionGatePolicy.ToRules(ReplyChoice.Nobody);
        Assert.IsNotNull(nobody);
        Assert.AreEqual(0, nobody.Count);
    }

    [TestMethod]
    public void ToRules_LimitedListsEachTickedGroup()
    {
        var choice = new ReplyChoice(ReplyAudience.Limited, Mentioned: true, Following: true, ListUris: ["at://a/list/1"]);

        var rules = InteractionGatePolicy.ToRules(choice)!;

        CollectionAssert.AreEqual(
            new[] { GateRuleKind.Mentioned, GateRuleKind.Following, GateRuleKind.List },
            rules.Select(r => r.Kind).ToArray());
        Assert.AreEqual("at://a/list/1", rules[2].ListUri);
    }

    [TestMethod]
    public void ToRules_LimitedWithNothingTickedMeansNobody()
    {
        var rules = InteractionGatePolicy.ToRules(new ReplyChoice(ReplyAudience.Limited))!;

        Assert.AreEqual(0, rules.Count);
        Assert.AreEqual(ReplyAudience.Nobody, InteractionGatePolicy.Normalize(new ReplyChoice(ReplyAudience.Limited)).Audience);
    }

    [TestMethod]
    public void ToRules_DropsDuplicateAndBlankListsAndCapsAtFive()
    {
        var choice = new ReplyChoice(
            ReplyAudience.Limited, Mentioned: true, Followers: true, Following: true,
            ListUris: ["l1", "l1", "", "l2", "l3"]);

        var rules = InteractionGatePolicy.ToRules(choice)!;

        Assert.AreEqual(InteractionGatePolicy.MaxRules, rules.Count);
        Assert.AreEqual(1, rules.Count(r => r.ListUri == "l1"));
    }

    [TestMethod]
    public void FromRules_RoundTripsEveryShape()
    {
        Assert.AreEqual(ReplyAudience.Everyone, InteractionGatePolicy.FromRules(null).Audience);
        Assert.AreEqual(ReplyAudience.Nobody, InteractionGatePolicy.FromRules([]).Audience);

        var choice = new ReplyChoice(ReplyAudience.Limited, Followers: true, ListUris: ["l1", "l2"]);
        var back = InteractionGatePolicy.FromRules(InteractionGatePolicy.ToRules(choice));

        Assert.AreEqual(ReplyAudience.Limited, back.Audience);
        Assert.IsFalse(back.Mentioned);
        Assert.IsTrue(back.Followers);
        Assert.IsFalse(back.Following);
        CollectionAssert.AreEqual(new[] { "l1", "l2" }, back.Lists.ToArray());
    }

    [TestMethod]
    public void CanTickList_StopsAtTheRuleLimit()
    {
        var choice = new ReplyChoice(ReplyAudience.Limited, Mentioned: true, Followers: true, ListUris: ["a", "b"]);
        Assert.IsTrue(InteractionGatePolicy.CanTickList(choice));

        Assert.IsFalse(InteractionGatePolicy.CanTickList(choice with { Following = true }));
    }

    [TestMethod]
    public void AreSame_IgnoresListOrderAndEmptyLimited()
    {
        var a = new ReplyChoice(ReplyAudience.Limited, Followers: true, ListUris: ["l1", "l2"]);
        var b = new ReplyChoice(ReplyAudience.Limited, Followers: true, ListUris: ["l2", "l1"]);

        Assert.IsTrue(InteractionGatePolicy.AreSame(a, b));
        Assert.IsFalse(InteractionGatePolicy.AreSame(a, a with { Mentioned = true }));
        Assert.IsTrue(InteractionGatePolicy.AreSame(new ReplyChoice(ReplyAudience.Limited), ReplyChoice.Nobody));
        Assert.IsFalse(InteractionGatePolicy.AreSame(ReplyChoice.Everyone, ReplyChoice.Nobody));
    }

    [TestMethod]
    public void Needs_GatesOnlyWhenNotTheDefault()
    {
        Assert.IsFalse(InteractionGatePolicy.NeedsThreadGate(ReplyChoice.Everyone));
        Assert.IsTrue(InteractionGatePolicy.NeedsThreadGate(ReplyChoice.Nobody));
        Assert.IsTrue(InteractionGatePolicy.NeedsThreadGate(new ReplyChoice(ReplyAudience.Limited, Followers: true)));
        Assert.IsFalse(InteractionGatePolicy.NeedsPostGate(allowQuotes: true));
        Assert.IsTrue(InteractionGatePolicy.NeedsPostGate(allowQuotes: false));
    }

    [TestMethod]
    public void Placement_ReplyControlsOnRootsOfOwnPostsOnly()
    {
        Assert.IsTrue(InteractionGatePolicy.ShowsReplyControl(isReply: false));
        Assert.IsFalse(InteractionGatePolicy.ShowsReplyControl(isReply: true));

        Assert.IsTrue(InteractionGatePolicy.CanEditReplySettings(own: true, isThreadRoot: true));
        Assert.IsFalse(InteractionGatePolicy.CanEditReplySettings(own: true, isThreadRoot: false));
        Assert.IsFalse(InteractionGatePolicy.CanEditReplySettings(own: false, isThreadRoot: true));

        Assert.IsTrue(InteractionGatePolicy.CanEditQuoteSettings(own: true));
        Assert.IsFalse(InteractionGatePolicy.CanEditQuoteSettings(own: false));
    }

    [TestMethod]
    public void IsDefault_OnlyEveryoneWithQuotesOn()
    {
        Assert.IsTrue(InteractionGatePolicy.IsDefault(ReplyChoice.Everyone, allowQuotes: true));
        Assert.IsFalse(InteractionGatePolicy.IsDefault(ReplyChoice.Everyone, allowQuotes: false));
        Assert.IsFalse(InteractionGatePolicy.IsDefault(ReplyChoice.Nobody, allowQuotes: true));
    }

    [TestMethod]
    public void Summary_ReadsNaturally()
    {
        Assert.AreEqual("Anyone can reply", InteractionGatePolicy.ReplySummary(ReplyChoice.Everyone));
        Assert.AreEqual("Replies are off", InteractionGatePolicy.ReplySummary(ReplyChoice.Nobody));
        Assert.AreEqual("Replies from followers", InteractionGatePolicy.ReplySummary(new ReplyChoice(ReplyAudience.Limited, Followers: true)));
        Assert.AreEqual(
            "Replies from people mentioned, followers and 2 lists",
            InteractionGatePolicy.ReplySummary(new ReplyChoice(ReplyAudience.Limited, Mentioned: true, Followers: true, ListUris: ["a", "b"])));
        Assert.AreEqual("Anyone can reply · Quotes are off", InteractionGatePolicy.Summary(ReplyChoice.Everyone, false, isReply: false));
        Assert.AreEqual("Quotes allowed", InteractionGatePolicy.Summary(ReplyChoice.Nobody, true, isReply: true));
    }
}

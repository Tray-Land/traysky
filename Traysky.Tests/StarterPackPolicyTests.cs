using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class StarterPackPolicyTests
{
    private const string Me = "did:plc:me";

    [TestMethod]
    public void FollowTargets_SkipsSelfFollowedAndBlocked()
    {
        PackMember[] members =
        [
            new("did:plc:a"),
            new(Me),
            new("did:plc:b", IsFollowing: true),
            new("did:plc:c", IsBlocking: true),
            new("did:plc:d", IsBlockedBy: true),
            new("did:plc:e")
        ];

        CollectionAssert.AreEqual(new[] { "did:plc:a", "did:plc:e" }, StarterPackPolicy.FollowTargets(members, Me).ToArray());
    }

    [TestMethod]
    public void FollowTargets_DedupesAndKeepsOrder()
    {
        PackMember[] members = [new("did:plc:b"), new("did:plc:a"), new("did:plc:b"), new(" ")];

        CollectionAssert.AreEqual(new[] { "did:plc:b", "did:plc:a" }, StarterPackPolicy.FollowTargets(members, null).ToArray());
    }

    [TestMethod]
    public void FollowTargets_EmptyWhenEveryoneIsFollowed()
    {
        Assert.AreEqual(0, StarterPackPolicy.FollowTargets([new("did:plc:a", IsFollowing: true)], Me).Count);
        Assert.AreEqual(0, StarterPackPolicy.FollowTargets([], Me).Count);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 1)]
    [DataRow(10, 1)]
    [DataRow(11, 2)]
    [DataRow(150, 15)]
    public void Chunk_SplitsIntoRunsOfAtMostSize(int count, int expectedChunks)
    {
        int[] items = Enumerable.Range(0, count).ToArray();

        var chunks = StarterPackPolicy.Chunk(items, StarterPackPolicy.FollowBatchSize);

        Assert.AreEqual(expectedChunks, chunks.Count);
        Assert.IsTrue(chunks.All(c => c.Count is >= 1 and <= StarterPackPolicy.FollowBatchSize));
        CollectionAssert.AreEqual(items, chunks.SelectMany(c => c).ToArray());
    }

    [TestMethod]
    public void Chunk_RejectsNonPositiveSize()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StarterPackPolicy.Chunk(new[] { 1 }, 0));
    }

    [TestMethod]
    public void PlanLookups_BatchesAt25AndDedupes()
    {
        string[] uris = Enumerable.Range(0, 60).Select(i => $"at://did:plc:x/app.bsky.graph.starterpack/{i}").ToArray();

        var batches = StarterPackPolicy.PlanLookups(uris.Concat([uris[0], null, " "]));

        CollectionAssert.AreEqual(new[] { 25, 25, 10 }, batches.Select(b => b.Count).ToArray());
        Assert.AreEqual(uris[0], batches[0][0]);
    }

    [TestMethod]
    public void IsRateLimited_Only429()
    {
        Assert.IsTrue(StarterPackPolicy.IsRateLimited(429));
        Assert.IsFalse(StarterPackPolicy.IsRateLimited(400));
        Assert.IsFalse(StarterPackPolicy.IsRateLimited(0));
    }

    [TestMethod]
    public void Labels_ReflectCounts()
    {
        Assert.AreEqual("Follow all", StarterPackPolicy.FollowAllLabel(0));
        Assert.AreEqual("Follow all (12)", StarterPackPolicy.FollowAllLabel(12));
        Assert.AreEqual("Following 3 of 12…", StarterPackPolicy.ProgressText(3, 12));
    }

    [TestMethod]
    public void SummaryText_CoversEveryOutcome()
    {
        Assert.AreEqual("Followed 1 account.", StarterPackPolicy.SummaryText(1, 0, 1, false, false));
        Assert.AreEqual("Followed 5 accounts.", StarterPackPolicy.SummaryText(5, 0, 5, false, false));
        Assert.AreEqual("Followed 4 accounts; 1 couldn't be followed.", StarterPackPolicy.SummaryText(4, 1, 5, false, false));
        Assert.AreEqual("Stopped after following 2 accounts (3 left).", StarterPackPolicy.SummaryText(2, 0, 5, true, false));
        Assert.AreEqual("You already follow everyone here.", StarterPackPolicy.SummaryText(0, 0, 0, false, false));
        StringAssert.Contains(StarterPackPolicy.SummaryText(2, 0, 5, false, true), "slow down");
    }

    [TestMethod]
    [DataRow("https://bsky.app/starter-pack/alice.bsky.social/3kabc", "alice.bsky.social", "3kabc")]
    [DataRow("  https://bsky.app/starter-pack/did:plc:xyz/3kabc?ref=1  ", "did:plc:xyz", "3kabc")]
    [DataRow("https://bsky.app/start/alice.bsky.social/3kabc", "alice.bsky.social", "3kabc")]
    [DataRow("HTTPS://WWW.BSKY.APP/starter-pack/alice.bsky.social/3kabc/", "alice.bsky.social", "3kabc")]
    [DataRow("at://did:plc:xyz/app.bsky.graph.starterpack/3kabc", "did:plc:xyz", "3kabc")]
    public void TryParseLink_AcceptsPackLinks(string input, string authority, string rkey)
    {
        Assert.IsTrue(StarterPackPolicy.TryParseLink(input, out string? a, out string? r), input);
        Assert.AreEqual(authority, a);
        Assert.AreEqual(rkey, r);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("starter pack")]
    [DataRow("https://bsky.app/profile/alice.bsky.social")]
    [DataRow("https://bsky.app/starter-pack/alice.bsky.social")]
    [DataRow("https://example.com/starter-pack/alice.bsky.social/3kabc")]
    [DataRow("ftp://bsky.app/starter-pack/alice.bsky.social/3kabc")]
    [DataRow("at://did:plc:xyz/app.bsky.feed.post/3kabc")]
    [DataRow("at://did:plc:xyz/app.bsky.graph.starterpack")]
    public void TryParseLink_RejectsEverythingElse(string? input)
    {
        Assert.IsFalse(StarterPackPolicy.TryParseLink(input, out _, out _), input);
    }

    [TestMethod]
    public void AtUriFromLink_RoundTrips()
    {
        Assert.AreEqual(
            "at://alice.bsky.social/app.bsky.graph.starterpack/3kabc",
            StarterPackPolicy.AtUriFromLink("https://bsky.app/starter-pack/alice.bsky.social/3kabc"));
        Assert.IsNull(StarterPackPolicy.AtUriFromLink("https://bsky.app/"));
    }

    [TestMethod]
    public void DeepLink_StarterPackForms()
    {
        Assert.IsTrue(DeepLinkPolicy.TryParse(new Uri("traysky://starterpack/alice.bsky.social/3kabc"), out DeepLink? path));
        Assert.AreEqual(DeepLinkKind.StarterPack, path!.Kind);
        Assert.AreEqual("at://alice.bsky.social/app.bsky.graph.starterpack/3kabc", path.StarterPackAtUri);

        Assert.IsTrue(DeepLinkPolicy.TryParse(
            new Uri("traysky:starterpack?uri=at://did:plc:xyz/app.bsky.graph.starterpack/3kabc"), out DeepLink? query));
        Assert.AreEqual("at://did:plc:xyz/app.bsky.graph.starterpack/3kabc", query!.StarterPackAtUri);

        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri("traysky://starterpack"), out _));
        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri("traysky://starterpack/alice.bsky.social"), out _));
    }
}

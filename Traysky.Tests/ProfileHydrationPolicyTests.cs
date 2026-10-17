using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ProfileHydrationPolicyTests
{
    private static List<string> Dids(int count) =>
        Enumerable.Range(0, count).Select(i => $"did:plc:user{i}").ToList();

    [TestMethod]
    public void NoActorsMakesNoBatches()
    {
        Assert.AreEqual(0, ProfileHydrationPolicy.PlanBatches([]).Count);
    }

    [TestMethod]
    public void OneActorIsOneBatch()
    {
        var batches = ProfileHydrationPolicy.PlanBatches(Dids(1));
        Assert.AreEqual(1, batches.Count);
        Assert.AreEqual(1, batches[0].Count);
    }

    [TestMethod]
    public void ExactlyOneFullBatchStaysOneBatch()
    {
        var batches = ProfileHydrationPolicy.PlanBatches(Dids(25));
        Assert.AreEqual(1, batches.Count);
        Assert.AreEqual(25, batches[0].Count);
    }

    [TestMethod]
    public void OneOverTheLimitSpillsIntoASecondBatch()
    {
        var batches = ProfileHydrationPolicy.PlanBatches(Dids(26));
        CollectionAssert.AreEqual(new[] { 25, 1 }, batches.Select(b => b.Count).ToArray());
    }

    [TestMethod]
    public void SixtyActorsMakeThreeBatches()
    {
        var batches = ProfileHydrationPolicy.PlanBatches(Dids(60));
        CollectionAssert.AreEqual(new[] { 25, 25, 10 }, batches.Select(b => b.Count).ToArray());
    }

    [TestMethod]
    public void OrderIsPreservedAcrossBatches()
    {
        List<string> input = Dids(30);
        List<string> flat = ProfileHydrationPolicy.PlanBatches(input).SelectMany(b => b).ToList();
        CollectionAssert.AreEqual(input, flat);
    }

    [TestMethod]
    public void DuplicatesAreDroppedKeepingTheFirst()
    {
        var batches = ProfileHydrationPolicy.PlanBatches(["did:plc:b", "did:plc:a", "did:plc:b", "did:plc:a"]);
        CollectionAssert.AreEqual(new[] { "did:plc:b", "did:plc:a" }, batches.Single().ToArray());
    }

    [TestMethod]
    public void DuplicatesAreFoundAfterNormalizing()
    {
        var batches = ProfileHydrationPolicy.PlanBatches(["Alice.Bsky.Social", "@alice.bsky.social", "  alice.bsky.social "]);
        CollectionAssert.AreEqual(new[] { "alice.bsky.social" }, batches.Single().ToArray());
    }

    [TestMethod]
    public void DuplicatesDoNotCountTowardsTheBatchLimit()
    {
        List<string> input = Dids(25);
        input.AddRange(Dids(25));
        Assert.AreEqual(1, ProfileHydrationPolicy.PlanBatches(input).Count);
    }

    [TestMethod]
    public void MixedDidsAndHandlesShareBatches()
    {
        var batches = ProfileHydrationPolicy.PlanBatches(["did:plc:abc", "bob.example.com", "did:web:example.com"]);
        CollectionAssert.AreEqual(new[] { "did:plc:abc", "bob.example.com", "did:web:example.com" }, batches.Single().ToArray());
    }

    [TestMethod]
    public void BlankEntriesAreSkipped()
    {
        var batches = ProfileHydrationPolicy.PlanBatches([null, "", "   ", "@", "did:plc:a"]);
        CollectionAssert.AreEqual(new[] { "did:plc:a" }, batches.Single().ToArray());
    }

    [TestMethod]
    public void CachedActorsAreSkipped()
    {
        var cached = new HashSet<string> { "did:plc:b" };
        var batches = ProfileHydrationPolicy.PlanBatches(["did:plc:a", "did:plc:b", "did:plc:c"], cached.Contains);
        CollectionAssert.AreEqual(new[] { "did:plc:a", "did:plc:c" }, batches.Single().ToArray());
    }

    [TestMethod]
    public void EverythingCachedMakesNoBatches()
    {
        Assert.AreEqual(0, ProfileHydrationPolicy.PlanBatches(Dids(40), _ => true).Count);
    }

    [TestMethod]
    public void CachedActorsDoNotCountTowardsTheBatchLimit()
    {
        // 30 inputs, 10 cached -> 20 to fetch -> one request, not two.
        var cached = Dids(10).ToHashSet();
        Assert.AreEqual(1, ProfileHydrationPolicy.PlanBatches(Dids(30), cached.Contains).Count);
    }

    [TestMethod]
    public void NormalizeLowercasesTrimsAndStripsAt()
    {
        Assert.AreEqual("alice.bsky.social", ProfileHydrationPolicy.Normalize(" @Alice.Bsky.Social "));
        Assert.IsNull(ProfileHydrationPolicy.Normalize(null));
        Assert.IsNull(ProfileHydrationPolicy.Normalize("  "));
    }
}

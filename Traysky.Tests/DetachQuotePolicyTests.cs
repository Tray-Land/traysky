using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class DetachQuotePolicyTests
{
    private const string Mine = "at://did:plc:me/app.bsky.feed.post/3k1";

    [TestMethod]
    public void CanDetach_OnlyWhenQuotedPostIsMine()
    {
        Assert.IsTrue(DetachQuotePolicy.CanDetach(Mine, "did:plc:me", signedIn: true));
        Assert.IsFalse(DetachQuotePolicy.CanDetach("at://did:plc:them/app.bsky.feed.post/3k1", "did:plc:me", true));
    }

    [TestMethod]
    public void CanDetach_FalseWhenSignedOutOrNoSession()
    {
        Assert.IsFalse(DetachQuotePolicy.CanDetach(Mine, "did:plc:me", signedIn: false));
        Assert.IsFalse(DetachQuotePolicy.CanDetach(Mine, null, true));
        Assert.IsFalse(DetachQuotePolicy.CanDetach(Mine, "", true));
    }

    [TestMethod]
    public void CanDetach_FalseWhenThereIsNoQuote()
    {
        Assert.IsFalse(DetachQuotePolicy.CanDetach(null, "did:plc:me", true));
        Assert.IsFalse(DetachQuotePolicy.CanDetach("", "did:plc:me", true));
    }

    [TestMethod]
    public void CanDetach_DidPrefixOfAnotherDidDoesNotMatch()
    {
        Assert.IsFalse(DetachQuotePolicy.CanDetach("at://did:plc:me2/app.bsky.feed.post/3k1", "did:plc:me", true));
    }

    [TestMethod]
    public void AuthorDid_ParsesOrReturnsNull()
    {
        Assert.AreEqual("did:plc:me", DetachQuotePolicy.AuthorDid(Mine));
        Assert.IsNull(DetachQuotePolicy.AuthorDid("https://bsky.app/profile/x/post/y"));
        Assert.IsNull(DetachQuotePolicy.AuthorDid("at://handle.example/app.bsky.feed.post/3k1"));
        Assert.IsNull(DetachQuotePolicy.AuthorDid("at://did:plc:me"));
        Assert.IsNull(DetachQuotePolicy.AuthorDid(null));
    }

    [TestMethod]
    public void Merge_AddsToEmptyAndNull()
    {
        var (r1, l1) = DetachQuotePolicy.Merge(null, "at://q", 50);
        Assert.AreEqual(DetachQuotePolicy.MergeResult.Added, r1);
        CollectionAssert.AreEqual(new[] { "at://q" }, l1.ToArray());

        var (r2, _) = DetachQuotePolicy.Merge([], "at://q", 50);
        Assert.AreEqual(DetachQuotePolicy.MergeResult.Added, r2);
    }

    [TestMethod]
    public void Merge_AppendsAndKeepsOrder()
    {
        var (r, l) = DetachQuotePolicy.Merge(["at://a", "at://b"], "at://c", 50);
        Assert.AreEqual(DetachQuotePolicy.MergeResult.Added, r);
        CollectionAssert.AreEqual(new[] { "at://a", "at://b", "at://c" }, l.ToArray());
    }

    [TestMethod]
    public void Merge_DuplicateIsAlreadyDetached()
    {
        var (r, l) = DetachQuotePolicy.Merge(["at://a"], "at://a", 50);
        Assert.AreEqual(DetachQuotePolicy.MergeResult.AlreadyDetached, r);
        Assert.AreEqual(1, l.Count);
    }

    [TestMethod]
    public void Merge_AtLimit_RefusesButDuplicateStillReportsDetached()
    {
        var (r, l) = DetachQuotePolicy.Merge(["at://a", "at://b"], "at://c", 2);
        Assert.AreEqual(DetachQuotePolicy.MergeResult.LimitReached, r);
        Assert.AreEqual(2, l.Count);

        Assert.AreEqual(DetachQuotePolicy.MergeResult.AlreadyDetached, DetachQuotePolicy.Merge(["at://a", "at://b"], "at://b", 2).Result);
    }
}

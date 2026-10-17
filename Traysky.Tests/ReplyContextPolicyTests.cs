using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ReplyContextPolicyTests
{
    [TestMethod]
    public void Classify_NoParent_IsNone()
    {
        Assert.AreEqual(ReplyContextShape.None, ReplyContextPolicy.Classify("root", null, null));
        Assert.AreEqual(ReplyContextShape.None, ReplyContextPolicy.Classify(null, "", null));
    }

    [TestMethod]
    public void Classify_ParentIsRoot_IsParentOnly()
    {
        Assert.AreEqual(ReplyContextShape.ParentOnly, ReplyContextPolicy.Classify("a", "a", null));
        Assert.AreEqual(ReplyContextShape.ParentOnly, ReplyContextPolicy.Classify("a", "a", "a"));
    }

    [TestMethod]
    public void Classify_MissingRoot_IsParentOnly()
    {
        Assert.AreEqual(ReplyContextShape.ParentOnly, ReplyContextPolicy.Classify(null, "p", "r"));
    }

    [TestMethod]
    public void Classify_ParentWithNoParent_IsParentOnly()
    {
        Assert.AreEqual(ReplyContextShape.ParentOnly, ReplyContextPolicy.Classify("r", "p", null));
    }

    [TestMethod]
    public void Classify_ParentRepliesToRoot_IsRootAndParent()
    {
        Assert.AreEqual(ReplyContextShape.RootAndParent, ReplyContextPolicy.Classify("r", "p", "r"));
    }

    [TestMethod]
    public void Classify_Deeper_IsGap()
    {
        Assert.AreEqual(ReplyContextShape.RootGapParent, ReplyContextPolicy.Classify("r", "p", "x"));
    }

    [TestMethod]
    public void MissingBetween_ReturnsPostsBetweenRootAndParent()
    {
        IReadOnlyList<string>? missing = ReplyContextPolicy.MissingBetween(["r", "m1", "m2", "p"], "r", "p");

        CollectionAssert.AreEqual(new[] { "m1", "m2" }, missing!.ToArray());
        Assert.IsTrue(ReplyContextPolicy.ExpandsInPlace(missing));
    }

    [TestMethod]
    public void MissingBetween_RootNotInChain_IsNull()
    {
        IReadOnlyList<string>? missing = ReplyContextPolicy.MissingBetween(["a", "b", "c", "p"], "r", "p");

        Assert.IsNull(missing);
        Assert.IsFalse(ReplyContextPolicy.ExpandsInPlace(missing));
    }

    [TestMethod]
    public void MissingBetween_RootAfterParent_IsNull()
    {
        Assert.IsNull(ReplyContextPolicy.MissingBetween(["p", "r"], "r", "p"));
    }

    [TestMethod]
    public void ExpandsInPlace_AtMostTwo()
    {
        Assert.IsTrue(ReplyContextPolicy.ExpandsInPlace([]));
        Assert.IsTrue(ReplyContextPolicy.ExpandsInPlace(["a", "b"]));
        Assert.IsFalse(ReplyContextPolicy.ExpandsInPlace(["a", "b", "c"]));
    }
}

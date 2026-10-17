using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class KnownFollowersPolicyTests
{
    [TestMethod]
    public void NoKnownFollowers_IsEmpty()
    {
        Assert.AreEqual(string.Empty, KnownFollowersPolicy.Summary([], 0));
        Assert.AreEqual(string.Empty, KnownFollowersPolicy.Summary(["Ann"], 0));
    }

    [TestMethod]
    public void OneFollower_NamesThem()
    {
        Assert.AreEqual("Followed by Ann", KnownFollowersPolicy.Summary(["Ann"], 1));
    }

    [TestMethod]
    public void TwoFollowers_JoinedWithAnd()
    {
        Assert.AreEqual("Followed by Ann and Bob", KnownFollowersPolicy.Summary(["Ann", "Bob"], 2));
    }

    [TestMethod]
    public void MoreThanShown_CountsTheRest()
    {
        Assert.AreEqual("Followed by Ann, Bob and 3 others", KnownFollowersPolicy.Summary(["Ann", "Bob", "Cy"], 5));
        Assert.AreEqual("Followed by Ann, Bob and 1 other", KnownFollowersPolicy.Summary(["Ann", "Bob", "Cy"], 3));
    }

    [TestMethod]
    public void SingleSampleButLargerTotal()
    {
        Assert.AreEqual("Followed by Ann and 4 others", KnownFollowersPolicy.Summary(["Ann"], 5));
    }

    [TestMethod]
    public void NoNames_FallsBackToCount()
    {
        Assert.AreEqual("Followed by 1 person you follow", KnownFollowersPolicy.Summary([], 1));
        Assert.AreEqual("Followed by 7 people you follow", KnownFollowersPolicy.Summary([], 7));
        Assert.AreEqual("Followed by 2 people you follow", KnownFollowersPolicy.Summary(["", ""], 2));
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class SavedFeedPolicyTests
{
    private static SavedFeedEntry Feed(string id, string value, bool pinned) => new(id, SavedFeedPolicy.FeedType, value, pinned);

    private static List<SavedFeedEntry> Sample() =>
    [
        new("t", SavedFeedPolicy.TimelineType, "following", true),
        Feed("a", "at://a", true),
        Feed("b", "at://b", false),
        Feed("c", "at://c", true),
    ];

    private static string Ids(IEnumerable<SavedFeedEntry> entries) => string.Join(",", entries.Select(e => e.Id));

    [TestMethod]
    public void Pin_FlipsAnExistingSavedEntryInPlace()
    {
        List<SavedFeedEntry> result = SavedFeedPolicy.Pin(Sample(), SavedFeedPolicy.FeedType, "at://b", "new");

        Assert.AreEqual("t,a,b,c", Ids(result));
        Assert.IsTrue(result[2].Pinned);
    }

    [TestMethod]
    public void Pin_AppendsANewPinnedEntryWithTheGivenId()
    {
        List<SavedFeedEntry> result = SavedFeedPolicy.Pin(Sample(), SavedFeedPolicy.FeedType, "at://z", "new");

        Assert.AreEqual("t,a,b,c,new", Ids(result));
        Assert.IsTrue(result[^1].Pinned);
        Assert.AreEqual("at://z", result[^1].Value);
    }

    [TestMethod]
    public void Pin_AlreadyPinnedChangesNothing()
    {
        List<SavedFeedEntry> before = Sample();
        List<SavedFeedEntry> result = SavedFeedPolicy.Pin(before, SavedFeedPolicy.FeedType, "at://a", "new");

        CollectionAssert.AreEqual(before, result);
    }

    [TestMethod]
    public void Pin_SameValueButDifferentTypeIsADifferentFeed()
    {
        List<SavedFeedEntry> result = SavedFeedPolicy.Pin(Sample(), SavedFeedPolicy.ListType, "at://a", "new");

        Assert.AreEqual(5, result.Count);
    }

    [TestMethod]
    public void Unpin_KeepsTheEntrySaved()
    {
        List<SavedFeedEntry> result = SavedFeedPolicy.Unpin(Sample(), SavedFeedPolicy.FeedType, "at://a");

        Assert.AreEqual("t,a,b,c", Ids(result));
        Assert.IsFalse(result[1].Pinned);
        Assert.IsTrue(result[3].Pinned);
    }

    [TestMethod]
    public void Remove_DropsOnlyThatEntry()
    {
        Assert.AreEqual("t,a,c", Ids(SavedFeedPolicy.Remove(Sample(), "b")));
        Assert.AreEqual("t,a,b,c", Ids(SavedFeedPolicy.Remove(Sample(), "missing")));
    }

    [TestMethod]
    public void IsPinned_OnlyCountsPinnedEntries()
    {
        List<SavedFeedEntry> entries = Sample();

        Assert.IsTrue(SavedFeedPolicy.IsPinned(entries, SavedFeedPolicy.FeedType, "at://a"));
        Assert.IsFalse(SavedFeedPolicy.IsPinned(entries, SavedFeedPolicy.FeedType, "at://b"));
        Assert.IsFalse(SavedFeedPolicy.IsPinned(entries, SavedFeedPolicy.FeedType, "at://z"));
    }

    [TestMethod]
    public void MovePinned_SwapsWithTheNextPinnedNeighbourSkippingSavedOnes()
    {
        // c is the last pinned entry; moving up swaps with a, hopping over the unpinned b.
        List<SavedFeedEntry> result = SavedFeedPolicy.MovePinned(Sample(), "c", -1);

        Assert.AreEqual("t,c,b,a", Ids(result));
    }

    [TestMethod]
    public void MovePinned_DownSwapsWithTheNextPinned()
    {
        Assert.AreEqual("a,t,b,c", Ids(SavedFeedPolicy.MovePinned(Sample(), "t", 1)));
    }

    [TestMethod]
    public void MovePinned_AtEitherEndOrUnpinnedChangesNothing()
    {
        Assert.AreEqual("t,a,b,c", Ids(SavedFeedPolicy.MovePinned(Sample(), "t", -1)));
        Assert.AreEqual("t,a,b,c", Ids(SavedFeedPolicy.MovePinned(Sample(), "c", 1)));
        Assert.AreEqual("t,a,b,c", Ids(SavedFeedPolicy.MovePinned(Sample(), "b", -1)));
        Assert.AreEqual("t,a,b,c", Ids(SavedFeedPolicy.MovePinned(Sample(), "missing", 1)));
        Assert.AreEqual("t,a,b,c", Ids(SavedFeedPolicy.MovePinned(Sample(), "a", 0)));
    }

    [TestMethod]
    public void CanMovePinned_MatchesWhatMovePinnedWouldDo()
    {
        List<SavedFeedEntry> entries = Sample();

        Assert.IsFalse(SavedFeedPolicy.CanMovePinned(entries, "t", -1));
        Assert.IsTrue(SavedFeedPolicy.CanMovePinned(entries, "t", 1));
        Assert.IsTrue(SavedFeedPolicy.CanMovePinned(entries, "c", -1));
        Assert.IsFalse(SavedFeedPolicy.CanMovePinned(entries, "c", 1));
        Assert.IsFalse(SavedFeedPolicy.CanMovePinned(entries, "b", -1));
        Assert.IsFalse(SavedFeedPolicy.CanMovePinned(entries, "b", 1));
    }

    [TestMethod]
    public void UnknownTypesSurviveEdits()
    {
        List<SavedFeedEntry> entries = [new("x", "future-type", "whatever", true), Feed("a", "at://a", false)];

        List<SavedFeedEntry> result = SavedFeedPolicy.Pin(entries, SavedFeedPolicy.FeedType, "at://a", "new");

        Assert.AreEqual(new SavedFeedEntry("x", "future-type", "whatever", true), result[0]);
    }

    [TestMethod]
    public void FromLegacy_PutsFollowingFirstThenPinnedThenSaved()
    {
        List<SavedFeedEntry> result = SavedFeedPolicy.FromLegacy(["at://p1", "at://p2"], ["at://s1", "at://p1", "at://s1"]);

        CollectionAssert.AreEqual(
            new[] { "following", "at://p1", "at://p2", "at://s1" },
            result.Select(e => e.Value).ToArray());
        CollectionAssert.AreEqual(new[] { true, true, true, false }, result.Select(e => e.Pinned).ToArray());
        Assert.AreEqual(SavedFeedPolicy.TimelineType, result[0].Type);
    }

    [TestMethod]
    public void FromLegacy_NothingSavedStillHasFollowing()
    {
        List<SavedFeedEntry> result = SavedFeedPolicy.FromLegacy([], []);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].Pinned);
    }

    [TestMethod]
    public void PinLabel_FollowsThePinnedState()
    {
        Assert.AreEqual("Pin", SavedFeedPolicy.PinLabel(false));
        Assert.AreEqual("Unpin", SavedFeedPolicy.PinLabel(true));
    }

    [TestMethod]
    public void NewId_IsShortAndUnique()
    {
        string first = SavedFeedPolicy.NewId();

        Assert.AreEqual(13, first.Length);
        Assert.AreNotEqual(first, SavedFeedPolicy.NewId());
    }
}

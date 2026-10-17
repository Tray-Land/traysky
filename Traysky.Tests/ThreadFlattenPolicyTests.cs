using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ThreadFlattenPolicyTests
{
    private static ThreadNode Post(int index, int depth, int more = 0, bool moreParents = false, bool gated = false, bool muted = false)
        => new(index, depth, ThreadNodeKind.Post, more, moreParents, gated, muted);

    private static ThreadNode Gone(int index, int depth, ThreadNodeKind kind = ThreadNodeKind.NotFound)
        => new(index, depth, kind);

    private static int[] Indexes(ThreadLayout layout) => layout.Entries.Select(e => e.Index).ToArray();

    [TestMethod]
    public void Sort_ApiValuesAndParsing()
    {
        Assert.AreEqual("top", ThreadSortOptions.ToApiValue(ThreadSort.Top));
        Assert.AreEqual("oldest", ThreadSortOptions.ToApiValue(ThreadSort.Oldest));
        Assert.AreEqual("newest", ThreadSortOptions.ToApiValue(ThreadSort.Newest));
        Assert.AreEqual(ThreadSort.Newest, ThreadSortOptions.Parse(" Newest "));
        Assert.AreEqual(ThreadSortOptions.Default, ThreadSortOptions.Parse(null));
        Assert.AreEqual(ThreadSortOptions.Default, ThreadSortOptions.Parse("sideways"));
    }

    [TestMethod]
    public void Empty_WhenNoAnchorOrAnchorUnavailable()
    {
        Assert.AreEqual(0, ThreadFlattenPolicy.Flatten([]).Entries.Count);
        Assert.AreEqual(0, ThreadFlattenPolicy.Flatten([Post(0, 1)]).Entries.Count);
        Assert.AreEqual(0, ThreadFlattenPolicy.Flatten([Gone(0, 0), Post(1, 1)]).Entries.Count);
    }

    [TestMethod]
    public void AnchorOnly_IsMainAtDepthZero()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten([Post(0, 0)]);

        Assert.AreEqual(1, layout.Entries.Count);
        Assert.IsTrue(layout.Entries[0].IsMain);
        Assert.AreEqual(0, layout.Entries[0].Depth);
        Assert.IsFalse(layout.AncestorsTruncated);
    }

    [TestMethod]
    public void Ancestors_OldestFirst_RegardlessOfInputOrder()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten([Post(0, -1), Post(1, -3), Post(2, -2), Post(3, 0), Post(4, 1)]);

        CollectionAssert.AreEqual(new[] { 1, 2, 0, 3, 4 }, Indexes(layout));
        Assert.IsTrue(layout.Entries[3].IsMain);
        Assert.AreEqual(0, layout.Entries[0].Depth);
    }

    [TestMethod]
    public void Ancestors_StopAtFirstGonePost_AndFlagTruncated()
    {
        // -3 is fine but sits above a deleted -2: only -1 survives.
        ThreadLayout layout = ThreadFlattenPolicy.Flatten([Post(0, -3), Gone(1, -2), Post(2, -1), Post(3, 0)]);

        CollectionAssert.AreEqual(new[] { 2, 3 }, Indexes(layout));
        Assert.IsTrue(layout.AncestorsTruncated);
    }

    [TestMethod]
    public void Ancestors_BlockedPostCutsChain()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten([Gone(0, -1, ThreadNodeKind.Blocked), Post(1, 0)]);

        CollectionAssert.AreEqual(new[] { 1 }, Indexes(layout));
        Assert.IsTrue(layout.AncestorsTruncated);
    }

    [TestMethod]
    public void Truncated_FromMoreParentsOnOutermostIncluded()
    {
        Assert.IsTrue(ThreadFlattenPolicy.Flatten([Post(0, -1, moreParents: true), Post(1, 0)]).AncestorsTruncated);
        Assert.IsTrue(ThreadFlattenPolicy.Flatten([Post(0, 0, moreParents: true)]).AncestorsTruncated);
        // An inner ancestor claiming more parents is moot once the outer one is present.
        Assert.IsFalse(ThreadFlattenPolicy.Flatten([Post(0, -2), Post(1, -1, moreParents: true), Post(2, 0)]).AncestorsTruncated);
    }

    [TestMethod]
    public void Replies_KeepServerOrderAndDepth()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten([Post(0, 0), Post(1, 1), Post(2, 2), Post(3, 3), Post(4, 1)]);

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, Indexes(layout));
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 1 }, layout.Entries.Select(e => e.Depth).ToArray());
    }

    [TestMethod]
    public void Replies_DepthIsCapped()
    {
        List<ThreadNode> nodes = [Post(0, 0)];
        for (int d = 1; d <= 10; d++)
            nodes.Add(Post(d, d));

        ThreadLayout layout = ThreadFlattenPolicy.Flatten(nodes);

        Assert.AreEqual(ThreadFlattenPolicy.MaxDisplayDepth, layout.Entries.Max(e => e.Depth));
        Assert.AreEqual(11, layout.Entries.Count);
    }

    [TestMethod]
    public void Replies_GonePostDropsItsWholeSubtree_NotItsSiblings()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten(
            [Post(0, 0), Post(1, 1), Gone(2, 2), Post(3, 3), Post(4, 3), Post(5, 2), Post(6, 1)]);

        CollectionAssert.AreEqual(new[] { 0, 1, 5, 6 }, Indexes(layout));
        Assert.AreEqual(0, layout.HiddenCount);
    }

    [TestMethod]
    public void Replies_BlockedAndUnavailableAreSkippedToo()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten(
            [Post(0, 0), Gone(1, 1, ThreadNodeKind.Blocked), Post(2, 2), Gone(3, 1, ThreadNodeKind.Unavailable), Post(4, 1)]);

        CollectionAssert.AreEqual(new[] { 0, 4 }, Indexes(layout));
    }

    [TestMethod]
    public void Replies_HiddenAndMutedAreCountedNotShown_WithTheirSubtree()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten(
            [Post(0, 0), Post(1, 1, gated: true), Post(2, 2), Post(3, 1, muted: true), Post(4, 1)]);

        CollectionAssert.AreEqual(new[] { 0, 4 }, Indexes(layout));
        Assert.AreEqual(2, layout.HiddenCount);
    }

    [TestMethod]
    public void Replies_IncludeHiddenShowsThem()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten(
            [Post(0, 0), Post(1, 1, gated: true), Post(2, 2), Post(3, 1, muted: true)], includeHidden: true);

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Indexes(layout));
        Assert.AreEqual(0, layout.HiddenCount);
    }

    [TestMethod]
    public void Replies_MoreRepliesCarriedOnReplyOnly()
    {
        ThreadLayout layout = ThreadFlattenPolicy.Flatten([Post(0, 0, more: 9), Post(1, 1, more: 4), Post(2, 1, more: -2)]);

        Assert.AreEqual(0, layout.Entries[0].MoreReplies);
        Assert.AreEqual(4, layout.Entries[1].MoreReplies);
        Assert.AreEqual(0, layout.Entries[2].MoreReplies);
    }

    [TestMethod]
    public void FlattenReplies_OtherEndpoint_StartsAtDepthOneWhateverTheServerNumbers()
    {
        (List<ThreadEntry> entries, int hidden) = ThreadFlattenPolicy.FlattenReplies(
            [Post(0, 1, gated: true), Post(1, 2), Post(2, 1, muted: true)], includeHidden: true, firstDepth: 1);

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, entries.Select(e => e.Index).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 1 }, entries.Select(e => e.Depth).ToArray());
        Assert.AreEqual(0, hidden);

        (entries, _) = ThreadFlattenPolicy.FlattenReplies([Post(0, 0), Post(1, 1)], includeHidden: true, firstDepth: 0);
        CollectionAssert.AreEqual(new[] { 1, 2 }, entries.Select(e => e.Depth).ToArray());
    }

    [TestMethod]
    public void MoreRepliesText()
    {
        Assert.AreEqual(string.Empty, ThreadFlattenPolicy.MoreRepliesText(0));
        Assert.AreEqual("Show 1 more reply", ThreadFlattenPolicy.MoreRepliesText(1));
        Assert.AreEqual("Show 12 more replies", ThreadFlattenPolicy.MoreRepliesText(12));
    }
}

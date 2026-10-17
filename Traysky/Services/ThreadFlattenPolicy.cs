using System;
using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>How the server orders the replies of a v2 thread.</summary>
public enum ThreadSort
{
    Top,
    Oldest,
    Newest
}

public static class ThreadSortOptions
{
    public const ThreadSort Default = ThreadSort.Top;

    public static string ToApiValue(ThreadSort sort) => sort switch
    {
        ThreadSort.Oldest => "oldest",
        ThreadSort.Newest => "newest",
        _ => "top"
    };

    /// <summary>Reads a stored value; anything unknown falls back to <see cref="Default"/>.</summary>
    public static ThreadSort Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "top" => ThreadSort.Top,
        "oldest" => ThreadSort.Oldest,
        "newest" => ThreadSort.Newest,
        _ => Default
    };

    public static string Label(ThreadSort sort) => sort switch
    {
        ThreadSort.Oldest => "Oldest replies",
        ThreadSort.Newest => "Newest replies",
        _ => "Top replies"
    };
}

public enum ThreadNodeKind
{
    Post,
    Blocked,
    NotFound,
    /// <summary>Not visible to this viewer, or a node type this app does not know.</summary>
    Unavailable
}

/// <summary>
/// One item of a v2 thread with everything the layout needs and nothing from the SDK, so the
/// rules can be tested. <paramref name="Index"/> points back into the caller's own item list.
/// </summary>
public sealed record ThreadNode(
    int Index,
    int Depth,
    ThreadNodeKind Kind,
    int MoreReplies = 0,
    bool MoreParents = false,
    bool HiddenByThreadGate = false,
    bool MutedByViewer = false);

/// <summary>A row to show. <paramref name="Depth"/> is the display indent level (0 for ancestors and the anchor).</summary>
public sealed record ThreadEntry(int Index, int Depth, bool IsMain, int MoreReplies);

/// <param name="HiddenCount">Replies left out because the author hid them or the viewer muted them.</param>
/// <param name="AncestorsTruncated">The chain above the anchor is incomplete (more parents exist, or one was gone).</param>
public sealed record ThreadLayout(IReadOnlyList<ThreadEntry> Entries, int HiddenCount, bool AncestorsTruncated)
{
    public static ThreadLayout Empty { get; } = new([], 0, false);
}

/// <summary>
/// Turns the flat, depth-annotated list from <c>getPostThreadV2</c> into the rows the post page
/// shows: ancestors oldest first, the anchor, then replies depth-first. The server numbers the
/// anchor 0, its parents -1, -2, … and replies 1, 2, …. Pure .NET on purpose.
/// </summary>
public static class ThreadFlattenPolicy
{
    /// <summary>Deeper replies still render, but at this indent so they stay readable.</summary>
    public const int MaxDisplayDepth = 6;

    public static ThreadLayout Flatten(IReadOnlyList<ThreadNode> nodes, bool includeHidden = false)
    {
        ThreadNode? anchor = nodes.FirstOrDefault(n => n.Depth == 0);
        if (anchor is null || anchor.Kind != ThreadNodeKind.Post)
            return ThreadLayout.Empty;

        // Ancestors: only the unbroken run nearest the anchor. A deleted or blocked post in the
        // chain cuts everything above it off, since its replies-to would be orphaned.
        List<ThreadNode> above = nodes.Where(n => n.Depth < 0).OrderBy(n => n.Depth).ToList();
        List<ThreadEntry> ancestors = [];
        bool truncated = anchor.MoreParents;
        for (int i = above.Count - 1; i >= 0; i--)
        {
            if (above[i].Kind != ThreadNodeKind.Post)
            {
                truncated = true;
                break;
            }

            ancestors.Insert(0, new ThreadEntry(above[i].Index, 0, false, 0));
            truncated = above[i].MoreParents;
        }

        List<ThreadEntry> entries = [.. ancestors];
        entries.Add(new ThreadEntry(anchor.Index, 0, true, 0));

        (List<ThreadEntry> replies, int hidden) = FlattenReplies(nodes, includeHidden, firstDepth: 1);
        entries.AddRange(replies);
        return new ThreadLayout(entries, hidden, truncated);
    }

    /// <summary>
    /// Replies only, for the "other replies" endpoint (no anchor, no ancestors). Nodes shallower
    /// than <paramref name="firstDepth"/> are ignored; the shallowest shown level displays at depth 1.
    /// </summary>
    public static (List<ThreadEntry> Entries, int HiddenCount) FlattenReplies(
        IReadOnlyList<ThreadNode> nodes, bool includeHidden, int firstDepth)
    {
        List<ThreadEntry> entries = [];
        int hidden = 0;
        int skipDepth = int.MaxValue;

        foreach (ThreadNode node in nodes)
        {
            if (node.Depth < firstDepth)
                continue;

            // Leaving a dropped subtree: the first node no deeper than the dropped one ends it.
            if (node.Depth <= skipDepth)
                skipDepth = int.MaxValue;
            else
                continue;

            if (node.Kind != ThreadNodeKind.Post)
            {
                skipDepth = node.Depth;
                continue;
            }

            if (!includeHidden && (node.HiddenByThreadGate || node.MutedByViewer))
            {
                hidden++;
                skipDepth = node.Depth;
                continue;
            }

            int display = Math.Clamp(node.Depth - firstDepth + 1, 1, MaxDisplayDepth);
            entries.Add(new ThreadEntry(node.Index, display, false, Math.Max(0, node.MoreReplies)));
        }

        return (entries, hidden);
    }

    /// <summary>Text for the "N more replies" link under a post whose replies were cut off.</summary>
    public static string MoreRepliesText(int count) => count switch
    {
        <= 0 => string.Empty,
        1 => "Show 1 more reply",
        _ => $"Show {count} more replies"
    };
}

using System;
using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>What a reply in the timeline shows above it. Pure .NET on purpose.</summary>
public enum ReplyContextShape
{
    /// <summary>Not a reply, or the context isn't available: the post stands alone.</summary>
    None,

    /// <summary>The post it replies to, then the reply.</summary>
    ParentOnly,

    /// <summary>The thread's first post, the post replied to, then the reply (nothing in between).</summary>
    RootAndParent,

    /// <summary>The thread's first post, a "Load more" gap, the post replied to, then the reply.</summary>
    RootGapParent
}

public static class ReplyContextPolicy
{
    /// <summary>
    /// A gap with more hidden posts than this opens the full thread page instead of expanding in
    /// place.
    /// </summary>
    public const int MaxInlineExpansion = 2;

    /// <summary>
    /// How many ancestors above the post replied to to ask the thread endpoint for when expanding
    /// a gap: the thread's first post sits that far up exactly when expansion stays in place.
    /// </summary>
    public const int ExpansionParentHeight = MaxInlineExpansion + 1;

    /// <summary>
    /// Picks the shape from the AT URIs the feed hands over: the thread's first post, the post
    /// replied to, and the post replied to's own parent (from its record). Null means that post
    /// wasn't available (deleted, blocked, or not a reply).
    /// </summary>
    public static ReplyContextShape Classify(string? rootUri, string? parentUri, string? parentsParentUri)
    {
        if (string.IsNullOrEmpty(parentUri))
            return ReplyContextShape.None;

        // The parent is itself the first post, or the root is missing: just show the parent.
        if (string.IsNullOrEmpty(rootUri) || rootUri == parentUri)
            return ReplyContextShape.ParentOnly;

        // The parent has no parent of its own, so it can't be below a different root.
        if (string.IsNullOrEmpty(parentsParentUri))
            return ReplyContextShape.ParentOnly;

        return parentsParentUri == rootUri
            ? ReplyContextShape.RootAndParent
            : ReplyContextShape.RootGapParent;
    }

    /// <summary>
    /// The posts strictly between the thread's first post and the post replied to, from a chain of
    /// AT URIs ordered oldest first that ends at the post replied to. Null when the chain doesn't
    /// reach the first post (too many posts hidden, or one of them is gone): the caller should
    /// open the full thread instead.
    /// </summary>
    public static IReadOnlyList<string>? MissingBetween(IReadOnlyList<string> chainOldestFirst, string rootUri, string parentUri)
    {
        int rootIndex = IndexOf(chainOldestFirst, rootUri);
        int parentIndex = IndexOf(chainOldestFirst, parentUri);
        if (rootIndex < 0 || parentIndex < 0 || rootIndex >= parentIndex)
            return null;

        List<string> missing = [];
        for (int i = rootIndex + 1; i < parentIndex; i++)
            missing.Add(chainOldestFirst[i]);
        return missing;
    }

    /// <summary>True when a gap of this many hidden posts is expanded where it is.</summary>
    public static bool ExpandsInPlace(IReadOnlyList<string>? missing) =>
        missing is not null && missing.Count <= MaxInlineExpansion;

    private static int IndexOf(IReadOnlyList<string> chain, string uri)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            if (string.Equals(chain[i], uri, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}

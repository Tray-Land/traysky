using System;
using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>
/// Detaching your post from someone else's quote of it: the quoted post's author lists the quoting
/// post in the <c>detachedEmbeddingUris</c> of their post gate. Pure .NET on purpose.
/// </summary>
public static class DetachQuotePolicy
{
    /// <summary>True when the viewer wrote the quoted post, so the quote can be detached by them.</summary>
    public static bool CanDetach(string? quotedPostUri, string? sessionDid, bool signedIn)
    {
        if (!signedIn || string.IsNullOrEmpty(sessionDid))
            return false;

        return string.Equals(AuthorDid(quotedPostUri), sessionDid, StringComparison.Ordinal);
    }

    /// <summary>The DID in <c>at://did:plc:xyz/app.bsky.feed.post/rkey</c>; null for anything that isn't a post AT URI.</summary>
    public static string? AuthorDid(string? atUri)
    {
        const string prefix = "at://";
        if (atUri is null || !atUri.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        string rest = atUri[prefix.Length..];
        int slash = rest.IndexOf('/');
        if (slash <= 0 || !rest.StartsWith("did:", StringComparison.Ordinal))
            return null;

        return rest[..slash];
    }

    public enum MergeResult
    {
        Added,
        AlreadyDetached,
        LimitReached
    }

    /// <summary>Adds <paramref name="quotingPostUri"/> to the detached list, keeping order and never duplicating.</summary>
    public static (MergeResult Result, IReadOnlyList<string> Detached) Merge(
        IEnumerable<string>? existing, string quotingPostUri, int max)
    {
        List<string> list = existing?.ToList() ?? [];
        if (list.Contains(quotingPostUri, StringComparer.Ordinal))
            return (MergeResult.AlreadyDetached, list);
        if (list.Count >= max)
            return (MergeResult.LimitReached, list);

        list.Add(quotingPostUri);
        return (MergeResult.Added, list);
    }
}

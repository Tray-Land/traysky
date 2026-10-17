using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Traysky.Services;

/// <summary>One account in a starter pack, with what the signed-in user already has with them.</summary>
public sealed record PackMember(string Did, bool IsFollowing = false, bool IsBlocking = false, bool IsBlockedBy = false);

/// <summary>
/// Pure logic behind starter packs: who "Follow all" should follow, how bulk follows are paced,
/// and recognizing a pack's web / AT links. No WinRT dependency, so it is linked into the tests.
/// </summary>
public static class StarterPackPolicy
{
    public const string Collection = "app.bsky.graph.starterpack";

    /// <summary>Follows issued back to back before pausing; keeps a 150-member pack well inside rate limits.</summary>
    public const int FollowBatchSize = 10;

    /// <summary>Pause between follow batches.</summary>
    public const int FollowBatchDelayMilliseconds = 400;

    /// <summary>The most packs one <c>getStarterPacks</c> request accepts.</summary>
    public const int MaxLookupBatchSize = 25;

    /// <summary>
    /// The DIDs "Follow all" should follow: everyone except yourself, accounts you already follow,
    /// and accounts blocked in either direction. Duplicates are dropped and order is kept.
    /// </summary>
    public static IReadOnlyList<string> FollowTargets(IEnumerable<PackMember> members, string? selfDid)
    {
        var targets = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (PackMember member in members)
        {
            if (string.IsNullOrWhiteSpace(member.Did)
                || member.IsFollowing || member.IsBlocking || member.IsBlockedBy
                || string.Equals(member.Did, selfDid, StringComparison.Ordinal)
                || !seen.Add(member.Did))
            {
                continue;
            }

            targets.Add(member.Did);
        }

        return targets;
    }

    /// <summary>Splits <paramref name="items"/> into runs of at most <paramref name="size"/>, in order.</summary>
    public static IReadOnlyList<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        if (size < 1)
            throw new ArgumentOutOfRangeException(nameof(size));

        var chunks = new List<IReadOnlyList<T>>();
        for (int i = 0; i < items.Count; i += size)
        {
            int count = Math.Min(size, items.Count - i);
            var chunk = new List<T>(count);
            for (int j = 0; j < count; j++)
                chunk.Add(items[i + j]);
            chunks.Add(chunk);
        }

        return chunks;
    }

    /// <summary>
    /// Pack AT URIs split into <see cref="MaxLookupBatchSize"/> lookups. Blank and duplicate
    /// entries are dropped; first-seen order is kept.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> PlanLookups(IEnumerable<string?> atUris)
    {
        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? uri in atUris)
        {
            if (!string.IsNullOrWhiteSpace(uri) && seen.Add(uri.Trim()))
                unique.Add(uri.Trim());
        }

        return Chunk(unique, MaxLookupBatchSize);
    }

    /// <summary>True when the server is telling us to slow down; the bulk follow stops rather than hammering it.</summary>
    public static bool IsRateLimited(int statusCode) => statusCode == 429;

    public static string FollowAllLabel(int eligible) =>
        eligible <= 0 ? "Follow all" : $"Follow all ({eligible:N0})";

    public static string ProgressText(int done, int total) => $"Following {done:N0} of {total:N0}…";

    /// <summary>The one-line outcome shown after a bulk follow ends, however it ended.</summary>
    public static string SummaryText(int followed, int failed, int total, bool cancelled, bool rateLimited)
    {
        string accounts = followed == 1 ? "1 account" : $"{followed:N0} accounts";

        if (rateLimited)
            return $"Followed {accounts}, then Bluesky asked us to slow down. Try again in a few minutes.";
        if (cancelled)
            return $"Stopped after following {accounts} ({total - followed - failed:N0} left).";
        if (failed > 0)
            return $"Followed {accounts}; {failed:N0} couldn't be followed.";
        return followed == 0 ? "You already follow everyone here." : $"Followed {accounts}.";
    }

    /// <summary>
    /// Reads a starter pack out of a bsky.app link (<c>/starter-pack/{actor}/{rkey}</c>, or the older
    /// <c>/start/{actor}/{rkey}</c>) or a pack AT URI. <paramref name="authority"/> is a handle or DID.
    /// </summary>
    public static bool TryParseLink(string? input, [NotNullWhen(true)] out string? authority, [NotNullWhen(true)] out string? rkey)
    {
        authority = rkey = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string text = input.Trim();

        if (text.StartsWith("at://", StringComparison.Ordinal))
        {
            if (!BlueskyLinks.TryParseAtUri(text, out string atAuthority, out string collection, out string atRkey)
                || collection != Collection || atAuthority.Length == 0 || atRkey.Length == 0)
            {
                return false;
            }

            authority = atAuthority;
            rkey = atRkey;
            return true;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || !BlueskyLinks.IsWebUri(uri)
            || !(uri.Host.Equals("bsky.app", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.Equals("www.bsky.app", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !(segments[0].Equals("starter-pack", StringComparison.OrdinalIgnoreCase)
                 || segments[0].Equals("start", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        authority = Uri.UnescapeDataString(segments[1]);
        rkey = Uri.UnescapeDataString(segments[2]);
        return authority.Length > 0 && rkey.Length > 0;
    }

    public static string ToAtUri(string authority, string rkey) => $"at://{authority}/{Collection}/{rkey}";

    /// <summary>The pack's AT URI when <paramref name="input"/> is a pack link, else null.</summary>
    public static string? AtUriFromLink(string? input) =>
        TryParseLink(input, out string? authority, out string? rkey) ? ToAtUri(authority, rkey) : null;
}

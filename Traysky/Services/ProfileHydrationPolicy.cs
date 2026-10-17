using System;
using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>
/// Batching rules for <c>getProfiles</c>, which takes at most 25 actors per request. Pure .NET
/// on purpose.
/// </summary>
public static class ProfileHydrationPolicy
{
    /// <summary>The most actors one <c>getProfiles</c> request accepts.</summary>
    public const int MaxBatchSize = 25;

    /// <summary>A cache/dedupe key for a DID or handle: trimmed, lower-cased, no leading '@'. Null when blank.</summary>
    public static string? Normalize(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return null;

        string trimmed = actor.Trim().TrimStart('@');
        return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Splits <paramref name="actors"/> into request-sized batches. Blank and duplicate entries are
    /// dropped, first-seen order is kept, and anything <paramref name="isCached"/> already has is skipped.
    /// Each entry is normalized (see <see cref="Normalize"/>).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> PlanBatches(IEnumerable<string?> actors, Func<string, bool>? isCached = null)
    {
        var batches = new List<IReadOnlyList<string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = new List<string>(MaxBatchSize);

        foreach (string? actor in actors)
        {
            string? key = Normalize(actor);
            if (key is null || !seen.Add(key) || (isCached?.Invoke(key) ?? false))
                continue;

            current.Add(key);
            if (current.Count == MaxBatchSize)
            {
                batches.Add(current);
                current = new List<string>(MaxBatchSize);
            }
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }
}

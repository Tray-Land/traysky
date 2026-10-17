using System;
using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>
/// Pure logic behind the search page: how a typed query is interpreted (a lone <c>#tag</c> goes
/// to the hashtag page instead of a text search) and the remembered recent queries (MRU order,
/// case-insensitive de-dup, capped).
/// </summary>
public static class SearchQueryPolicy
{
    public const int MaxRecents = 10;

    /// <summary>Longest query sent to the search endpoints.</summary>
    public const int MaxQueryLength = 300;

    /// <summary>Trims the query and collapses it to the length the endpoints accept.</summary>
    public static string Normalize(string? query)
    {
        string trimmed = (query ?? string.Empty).Trim();
        return trimmed.Length > MaxQueryLength ? trimmed[..MaxQueryLength].TrimEnd() : trimmed;
    }

    /// <summary>
    /// The tag (no leading #) when <paramref name="query"/> is exactly one hashtag such as
    /// <c>#dotnet</c>, which the hashtag page handles; otherwise null (a multi-word or plain query).
    /// </summary>
    public static string? AsHashtag(string? query)
    {
        string normalized = Normalize(query);
        if (normalized.Length < 2 || normalized[0] != '#')
            return null;

        string tag = normalized.TrimStart('#');
        if (tag.Length == 0)
            return null;

        foreach (char c in tag)
        {
            if (char.IsWhiteSpace(c) || c == '#')
                return null;
        }

        return tag;
    }

    /// <summary>Whether the actor typeahead is worth calling for what's been typed so far.</summary>
    public static bool ShouldSuggest(string? query)
    {
        string normalized = Normalize(query);
        return normalized.Length >= 2 && normalized[0] != '#';
    }

    /// <summary>Moves <paramref name="query"/> to the front of <paramref name="recents"/>, de-duping case-insensitively and capping the list.</summary>
    public static IReadOnlyList<string> WithRecentQuery(IReadOnlyList<string> recents, string? query)
    {
        string normalized = Normalize(query);
        if (normalized.Length == 0)
            return recents;

        var updated = new List<string>(recents.Count + 1) { normalized };
        foreach (string existing in recents)
        {
            if (!string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                updated.Add(existing);
        }

        if (updated.Count > MaxRecents)
            updated.RemoveRange(MaxRecents, updated.Count - MaxRecents);

        return updated;
    }

    /// <summary>Recent queries starting with <paramref name="prefix"/> (case-insensitive), most-recent first, excluding an exact match of what's typed.</summary>
    public static IReadOnlyList<string> FilterRecents(IReadOnlyList<string> recents, string? prefix)
    {
        string normalized = Normalize(prefix);
        if (normalized.Length == 0)
            return recents;

        var matches = new List<string>();
        foreach (string recent in recents)
        {
            if (recent.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(recent, normalized, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(recent);
            }
        }
        return matches;
    }

    /// <summary>The recents without <paramref name="query"/>.</summary>
    public static IReadOnlyList<string> WithoutRecentQuery(IReadOnlyList<string> recents, string? query)
    {
        string normalized = Normalize(query);
        var kept = new List<string>(recents.Count);
        foreach (string recent in recents)
        {
            if (!string.Equals(recent, normalized, StringComparison.OrdinalIgnoreCase))
                kept.Add(recent);
        }
        return kept;
    }
}

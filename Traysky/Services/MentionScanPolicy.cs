using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Traysky.Services;

/// <summary>Finds the accounts a draft mentions, so each can be reviewed before the post goes out.</summary>
public static partial class MentionScanPolicy
{
    // '@' at the start, after whitespace or after '(' (how Bluesky reads it), then a dotted handle that may contain hyphens.
    [GeneratedRegex(@"(?:^|[\s(])@([A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+)", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();

    /// <summary>Distinct handles (no '@') in order of first appearance, case-insensitively de-duplicated.</summary>
    public static IReadOnlyList<string> Handles(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<string> handles = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MentionRegex().Matches(text))
        {
            string handle = match.Groups[1].Value;
            if (seen.Add(handle))
                handles.Add(handle);
        }

        return handles;
    }
}

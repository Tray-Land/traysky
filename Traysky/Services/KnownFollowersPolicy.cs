using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>The "Followed by …" line under a profile header. Pure .NET on purpose.</summary>
public static class KnownFollowersPolicy
{
    private const int NamesShown = 2;

    /// <summary>
    /// "Followed by A, B and 3 others". <paramref name="names"/> are the sample the server returned
    /// (a handful at most); <paramref name="totalCount"/> is how many people you follow also follow them.
    /// </summary>
    public static string Summary(IReadOnlyList<string> names, int totalCount)
    {
        if (totalCount <= 0)
            return string.Empty;

        List<string> shown = names.Where(n => n.Length > 0).Take(NamesShown).ToList();
        int rest = totalCount - shown.Count;

        if (shown.Count == 0)
            return $"Followed by {totalCount} {(totalCount == 1 ? "person" : "people")} you follow";

        string others = rest == 1 ? "1 other" : $"{rest} others";
        return shown.Count switch
        {
            1 when rest <= 0 => $"Followed by {shown[0]}",
            1 => $"Followed by {shown[0]} and {others}",
            _ when rest <= 0 => $"Followed by {shown[0]} and {shown[1]}",
            _ => $"Followed by {shown[0]}, {shown[1]} and {others}"
        };
    }
}

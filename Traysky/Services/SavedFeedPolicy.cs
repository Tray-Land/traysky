using System;
using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>One entry of the account's saved-feeds preference, in the shape the service stores it.</summary>
public sealed record SavedFeedEntry(string Id, string Type, string Value, bool Pinned);

/// <summary>
/// Pin, unpin, reorder and remove on the saved-feeds list. Pure .NET: the preferences record is
/// read and written elsewhere, so these only decide what the list should look like afterwards.
/// Every edit returns a new list and leaves entries it doesn't touch exactly as they were, so a
/// type this app doesn't know about survives the round trip. Order in the list is the order the
/// pinned feeds show in, on bsky.app as well.
/// </summary>
public static class SavedFeedPolicy
{
    public const string FeedType = "feed";
    public const string ListType = "list";
    public const string TimelineType = "timeline";

    public static bool IsPinned(IEnumerable<SavedFeedEntry> entries, string type, string value) =>
        entries.Any(e => e.Pinned && Matches(e, type, value));

    /// <summary>Pins the feed: flips an existing saved entry, or appends a new pinned one with <paramref name="newId"/>.</summary>
    public static List<SavedFeedEntry> Pin(IEnumerable<SavedFeedEntry> entries, string type, string value, string newId)
    {
        List<SavedFeedEntry> list = [.. entries];
        int index = list.FindIndex(e => Matches(e, type, value));
        if (index < 0)
            list.Add(new SavedFeedEntry(newId, type, value, Pinned: true));
        else if (!list[index].Pinned)
            list[index] = list[index] with { Pinned = true };
        return list;
    }

    /// <summary>Unpins but keeps the feed saved, as bsky.app's "Unpin" does.</summary>
    public static List<SavedFeedEntry> Unpin(IEnumerable<SavedFeedEntry> entries, string type, string value) =>
        [.. entries.Select(e => Matches(e, type, value) && e.Pinned ? e with { Pinned = false } : e)];

    /// <summary>Drops the feed from the saved list altogether.</summary>
    public static List<SavedFeedEntry> Remove(IEnumerable<SavedFeedEntry> entries, string id) =>
        [.. entries.Where(e => e.Id != id)];

    /// <summary>
    /// Moves a pinned entry one place earlier (<paramref name="delta"/> -1) or later (+1) among the
    /// pinned ones by swapping it with its pinned neighbour; saved-but-unpinned entries stay put.
    /// Does nothing at either end or for an entry that isn't pinned.
    /// </summary>
    public static List<SavedFeedEntry> MovePinned(IEnumerable<SavedFeedEntry> entries, string id, int delta)
    {
        List<SavedFeedEntry> list = [.. entries];
        int from = list.FindIndex(e => e.Id == id && e.Pinned);
        if (from < 0 || delta == 0)
            return list;

        int step = delta < 0 ? -1 : 1;
        int to = from + step;
        while (to >= 0 && to < list.Count && !list[to].Pinned)
            to += step;

        if (to < 0 || to >= list.Count)
            return list;

        (list[from], list[to]) = (list[to], list[from]);
        return list;
    }

    public static bool CanMovePinned(IReadOnlyList<SavedFeedEntry> entries, string id, int delta)
    {
        int from = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Id == id && entries[i].Pinned)
            {
                from = i;
                break;
            }
        }
        if (from < 0 || delta == 0)
            return false;

        int step = delta < 0 ? -1 : 1;
        for (int i = from + step; i >= 0 && i < entries.Count; i += step)
        {
            if (entries[i].Pinned)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The list to start from when an account has no saved-feeds v2 record yet: Following first, then the
    /// legacy pinned feeds in order, then the legacy saved ones. A brand-new account gets just Following.
    /// </summary>
    public static List<SavedFeedEntry> FromLegacy(IEnumerable<string> pinnedUris, IEnumerable<string> savedUris)
    {
        List<SavedFeedEntry> list = [new SavedFeedEntry(NewId(), TimelineType, "following", Pinned: true)];
        HashSet<string> pinned = [.. pinnedUris];

        foreach (string uri in pinned)
            list.Add(new SavedFeedEntry(NewId(), FeedType, uri, Pinned: true));
        foreach (string uri in savedUris.Where(u => !pinned.Contains(u)).Distinct())
            list.Add(new SavedFeedEntry(NewId(), FeedType, uri, Pinned: false));

        return list;
    }

    /// <summary>The "Pin" / "Unpin" label for a feed's button.</summary>
    public static string PinLabel(bool pinned) => pinned ? "Unpin" : "Pin";

    /// <summary>A fresh entry id (the preference wants one per entry; bsky.app uses a short random string).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..13];

    private static bool Matches(SavedFeedEntry e, string type, string value) =>
        string.Equals(e.Type, type, StringComparison.Ordinal) && string.Equals(e.Value, value, StringComparison.Ordinal);
}

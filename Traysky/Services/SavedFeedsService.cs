using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>What the feeds manager shows for one saved entry.</summary>
public sealed record SavedFeedLabel(string Name, Uri? AvatarUri);

/// <summary>
/// Reads and edits the account's saved-feeds preference (pin, unpin, reorder, remove). The whole
/// preferences record is read, only the saved-feeds entry replaced, and the rest sent back as it
/// was, because <c>putPreferences</c> overwrites every preference type at once. The decisions about
/// what the list should look like live in <see cref="SavedFeedPolicy"/>.
/// </summary>
public static class SavedFeedsService
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static List<SavedFeedEntry>? _previewEntries;

    /// <summary>The saved feeds, or null when the lookup failed.</summary>
    public static async Task<List<SavedFeedEntry>?> LoadAsync()
    {
        if (PreviewMode.IsEnabled)
            return [.. PreviewEntries()];

        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        await _gate.WaitAsync();
        try
        {
            return (await ReadAsync())?.Entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static Task<List<SavedFeedEntry>?> PinAsync(string type, string value) =>
        UpdateAsync(entries => SavedFeedPolicy.Pin(entries, type, value, SavedFeedPolicy.NewId()));

    public static Task<List<SavedFeedEntry>?> UnpinAsync(string type, string value) =>
        UpdateAsync(entries => SavedFeedPolicy.Unpin(entries, type, value));

    /// <summary>
    /// Applies <paramref name="edit"/> to the saved feeds and stores the result, then refreshes the
    /// shell's tab row. Returns the stored list, or null when the account couldn't be reached or the
    /// service refused (nothing was changed in that case).
    /// </summary>
    public static async Task<List<SavedFeedEntry>?> UpdateAsync(Func<List<SavedFeedEntry>, List<SavedFeedEntry>> edit)
    {
        if (PreviewMode.IsEnabled)
        {
            _previewEntries = edit([.. PreviewEntries()]);
            return [.. _previewEntries];
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        List<SavedFeedEntry>? stored = null;
        await _gate.WaitAsync();
        try
        {
            Snapshot? current = await ReadAsync();
            if (current is null)
                return null;

            List<SavedFeedEntry> next = edit([.. current.Entries]);
            List<Preference> preferences = [.. current.Preferences.Where(p => p is not SavedFeedPreferencesV2)];
            preferences.Add(new SavedFeedPreferencesV2([.. next.Select(ToSavedFeed), .. current.Unknown]));

            var result = await BlueskySessionService.Instance.Agent.PutPreferences(new Preferences(preferences, false));
            if (!result.Succeeded)
            {
                LogService.Warn("Feeds", $"PutPreferences failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return null;
            }

            stored = next;
        }
        catch (Exception ex)
        {
            LogService.Warn("Feeds", $"PutPreferences threw: {ex.Message}");
            return null;
        }
        finally
        {
            _gate.Release();
        }

        await FeedsService.Instance.RefreshAsync();
        return stored;
    }

    /// <summary>Display names and avatars for the feeds and lists in <paramref name="entries"/>, keyed by entry value.</summary>
    public static async Task<Dictionary<string, SavedFeedLabel>> DescribeAsync(IEnumerable<SavedFeedEntry> entries)
    {
        Dictionary<string, SavedFeedLabel> labels = [];

        if (PreviewMode.IsEnabled)
        {
            foreach (KeyValuePair<string, SavedFeedLabel> pair in PreviewMode.SampleSavedFeedLabels())
                labels[pair.Key] = pair.Value;
            return labels;
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
            return labels;

        BlueskyAgent agent = BlueskySessionService.Instance.Agent;
        List<AtUri> feedUris = [.. ParseAll(entries.Where(e => e.Type == SavedFeedPolicy.FeedType))];
        List<AtUri> listUris = [.. ParseAll(entries.Where(e => e.Type == SavedFeedPolicy.ListType))];

        foreach ((string key, idunno.Bluesky.Feed.GeneratorView view) in await FeedsService.LoadGeneratorsAsync(agent, feedUris))
            labels[key] = new SavedFeedLabel(view.DisplayName, view.AvatarUri);
        foreach ((string key, idunno.Bluesky.Graph.ListView view) in await FeedsService.LoadListsAsync(agent, listUris))
            labels[key] = new SavedFeedLabel(view.Name, view.Avatar);

        return labels;
    }

    private static IEnumerable<AtUri> ParseAll(IEnumerable<SavedFeedEntry> entries)
    {
        foreach (SavedFeedEntry entry in entries)
        {
            AtUri? uri = null;
            try
            {
                uri = new AtUri(entry.Value);
            }
            catch (Exception)
            {
            }

            if (uri is not null)
                yield return uri;
        }
    }

    private sealed record Snapshot(Preferences Preferences, List<SavedFeedEntry> Entries, List<SavedFeed> Unknown);

    /// <summary>Reads the preferences. An account with no v2 record gets one derived from the legacy lists (or just Following).</summary>
    private static async Task<Snapshot?> ReadAsync()
    {
        try
        {
            var result = await BlueskySessionService.Instance.Agent.GetPreferences(includeBlueskyModerationLabeler: false);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Feeds", $"GetPreferences failed: {(int)result.StatusCode}");
                return null;
            }

            Preferences preferences = result.Result;
            List<SavedFeedEntry> entries;
            List<SavedFeed> unknown = [];
            if (preferences.SavedFeedsPreferenceV2 is { Count: > 0 } v2)
            {
                // A type this library can't name can't be rebuilt either; it is set aside and written back as it was.
                entries = [.. v2.Where(s => s.Type != SavedFeedPreferenceType.Unknown).Select(s => new SavedFeedEntry(s.Id, TypeName(s.Type), s.Value, s.Pinned))];
                unknown = [.. v2.Where(s => s.Type == SavedFeedPreferenceType.Unknown)];
            }
            else
            {
                var legacy = preferences.SavedFeedsPreference;
                entries = SavedFeedPolicy.FromLegacy(
                    legacy?.Pinned.Select(u => u.ToString()) ?? [],
                    legacy?.Saved.Select(u => u.ToString()) ?? []);
            }

            return new Snapshot(preferences, entries, unknown);
        }
        catch (Exception ex)
        {
            LogService.Warn("Feeds", $"GetPreferences threw: {ex.Message}");
            return null;
        }
    }

    private static SavedFeed ToSavedFeed(SavedFeedEntry entry) => new(entry.Id, ToType(entry.Type), entry.Value, entry.Pinned);

    private static string TypeName(SavedFeedPreferenceType type) => type switch
    {
        SavedFeedPreferenceType.Feed => SavedFeedPolicy.FeedType,
        SavedFeedPreferenceType.List => SavedFeedPolicy.ListType,
        _ => SavedFeedPolicy.TimelineType
    };

    private static SavedFeedPreferenceType ToType(string type) => type switch
    {
        SavedFeedPolicy.FeedType => SavedFeedPreferenceType.Feed,
        SavedFeedPolicy.ListType => SavedFeedPreferenceType.List,
        _ => SavedFeedPreferenceType.Timeline
    };

    private static List<SavedFeedEntry> PreviewEntries() => _previewEntries ??= PreviewMode.SampleSavedFeeds();
}

using idunno.AtProto;
using idunno.Bluesky.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>
/// Hydrates many actors at once with <c>getProfiles</c> (25 per request) instead of one
/// <c>getProfile</c> each. Kept in memory only, capped, and emptied by <see cref="MemoryTrimService"/>
/// when the popup has been hidden for a while. Profiles are looked up by DID or handle.
/// </summary>
public sealed class ProfileCacheService
{
    /// <summary>Enough for a few screens of rows; the cache is a request saver, not a store.</summary>
    private const int MaxEntries = 300;

    public static ProfileCacheService Instance { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, ProfileViewDetailed> _byKey = new(StringComparer.Ordinal);

    private ProfileCacheService()
    {
    }

    /// <summary>
    /// Profiles for <paramref name="actors"/> (DIDs or handles), keyed by the normalized form of each
    /// identifier passed in (see <see cref="ProfileHydrationPolicy.Normalize"/>). Actors the server
    /// doesn't return (deleted, blocked, unresolvable) are simply absent. A failed batch is logged
    /// and skipped, so the result is whatever could be loaded.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ProfileViewDetailed>> GetManyAsync(
        IEnumerable<string?> actors,
        CancellationToken cancellationToken = default)
    {
        List<string> requested = actors.Select(ProfileHydrationPolicy.Normalize).OfType<string>().Distinct().ToList();
        var found = new Dictionary<string, ProfileViewDetailed>(StringComparer.Ordinal);

        IReadOnlyList<IReadOnlyList<string>> batches;
        lock (_gate)
        {
            foreach (string key in requested)
            {
                if (_byKey.TryGetValue(key, out ProfileViewDetailed? hit))
                    found[key] = hit;
            }

            batches = ProfileHydrationPolicy.PlanBatches(requested, _byKey.ContainsKey);
        }

        if (batches.Count == 0 || PreviewMode.IsEnabled || !BlueskySessionService.Instance.IsSignedIn)
            return found;

        await LabelService.Instance.EnsureLoadedAsync();

        foreach (IReadOnlyList<string> batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await BlueskySessionService.Instance.Agent.GetProfiles(
                    batch.Select(ToIdentifier).ToList(),
                    LabelService.Instance.Subscribed,
                    cancellationToken);

                if (!result.Succeeded || result.Result is null)
                {
                    LogService.Warn("ProfileCache", $"GetProfiles failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    continue;
                }

                lock (_gate)
                {
                    foreach (ProfileViewDetailed profile in result.Result)
                        Store(profile);

                    // Report under the identifier the caller used, whether that was the DID or the handle.
                    foreach (string key in batch)
                    {
                        if (_byKey.TryGetValue(key, out ProfileViewDetailed? stored))
                            found[key] = stored;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogService.Warn("ProfileCache", $"GetProfiles threw: {ex.Message}");
            }
        }

        return found;
    }

    /// <summary>Drops everything. Called when the popup has been hidden long enough to trim memory.</summary>
    public void Clear()
    {
        lock (_gate)
            _byKey.Clear();
    }

    private void Store(ProfileViewDetailed profile)
    {
        if (_byKey.Count >= MaxEntries)
            _byKey.Clear();

        _byKey[ProfileHydrationPolicy.Normalize(profile.Did.ToString())!] = profile;
        _byKey[ProfileHydrationPolicy.Normalize(profile.Handle.ToString())!] = profile;
    }

    private static AtIdentifier ToIdentifier(string key) =>
        key.StartsWith("did:", StringComparison.Ordinal)
            ? new Did(key)
            : new Handle(key);
}

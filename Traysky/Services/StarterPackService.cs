using idunno.AtProto;
using idunno.Bluesky.Graph;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>A pack as the detail page shows it: the row model plus the feeds bundled with it.</summary>
public sealed record StarterPackDetail(StarterPackItem Pack, IReadOnlyList<string> FeedNames);

/// <summary>Looks starter packs up by AT URI, for deep links and pack pages. Nothing is cached.</summary>
public static class StarterPackService
{
    /// <summary>One pack by its <c>app.bsky.graph.starterpack</c> AT URI (handle or DID authority). Null when it couldn't be loaded.</summary>
    public static async Task<StarterPackDetail?> GetAsync(string atUri, CancellationToken cancellationToken = default)
    {
        if (PreviewMode.IsEnabled || !BlueskySessionService.Instance.IsSignedIn || string.IsNullOrWhiteSpace(atUri))
            return null;

        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            AtProtoHttpResult<StarterPackView> result = await BlueskySessionService.Instance.Agent.GetStarterPack(
                new AtUri(atUri), LabelService.Instance.Subscribed, cancellationToken);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("StarterPack", $"GetStarterPack failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return null;
            }

            StarterPackView view = result.Result;
            string[] feeds = (view.Feeds ?? [])
                .Select(feed => feed.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            return new StarterPackDetail(FeedMapper.ToStarterPackItem(view), feeds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogService.Warn("StarterPack", $"GetStarterPack threw: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Many packs with <c>getStarterPacks</c> (25 per request) rather than one request each. A failed
    /// batch is logged and skipped, so the result is whatever could be loaded. Not used by a screen yet.
    /// </summary>
    public static async Task<IReadOnlyList<StarterPackItem>> GetManyAsync(
        IEnumerable<string?> atUris,
        CancellationToken cancellationToken = default)
    {
        var packs = new List<StarterPackItem>();
        if (PreviewMode.IsEnabled || !BlueskySessionService.Instance.IsSignedIn)
            return packs;

        await LabelService.Instance.EnsureLoadedAsync();

        foreach (IReadOnlyList<string> batch in StarterPackPolicy.PlanLookups(atUris))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await BlueskySessionService.Instance.Agent.GetStarterPacks(
                    batch.Select(uri => new AtUri(uri)).ToList(), LabelService.Instance.Subscribed, cancellationToken);

                if (!result.Succeeded || result.Result is null)
                {
                    LogService.Warn("StarterPack", $"GetStarterPacks failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    continue;
                }

                foreach (StarterPackViewBasic pack in result.Result)
                    packs.Add(FeedMapper.ToStarterPackItem(pack));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogService.Warn("StarterPack", $"GetStarterPacks threw: {ex.Message}");
            }
        }

        return packs;
    }
}

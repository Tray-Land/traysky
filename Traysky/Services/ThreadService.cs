using idunno.AtProto;
using idunno.Bluesky.Feed;
using idunno.Bluesky.Unspecced;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>What a thread fetch produced.</summary>
/// <param name="UsedV2">False when the rows came from the older nested endpoint.</param>
/// <param name="HasHiddenReplies">There are replies the page can fetch on demand (author-hidden, muted, or otherwise left out).</param>
/// <param name="Status">HTTP status of the call that decided the outcome; 0 for a transport failure.</param>
public sealed record ThreadLoadResult(List<ThreadRow> Rows, bool UsedV2, bool HasHiddenReplies, HttpStatusCode Status)
{
    public bool Succeeded => Rows.Count > 0;
}

/// <summary>
/// Loads a post's thread: the v2 endpoint when <see cref="SettingsService.UseThreadV2"/> is on,
/// falling back to the nested v1 endpoint if v2 fails for any reason other than the post not
/// existing. Network only; layout rules live in <see cref="ThreadFlattenPolicy"/>.
/// </summary>
public static class ThreadService
{
    /// <summary>Levels of replies requested below the anchor (the API allows up to 20).</summary>
    public const int Below = 6;

    public const int BranchingFactor = 10;

    private const int V1ReplyDepth = 3;

    public static async Task<ThreadLoadResult> LoadAsync(string atUri, ThreadSort sort)
    {
        idunno.Bluesky.BlueskyAgent agent = BlueskySessionService.Instance.Agent;
        await LabelService.Instance.EnsureLoadedAsync();

        if (SettingsService.UseThreadV2)
        {
            try
            {
                AtProtoHttpResult<PostThreadV2> v2 = await agent.GetPostThreadV2(
                    new AtUri(atUri),
                    above: true,
                    below: Below,
                    branchingFactor: BranchingFactor,
                    sort: ThreadSortOptions.ToApiValue(sort),
                    subscribedLabelers: LabelService.Instance.Subscribed);

                if (v2.Succeeded && v2.Result is not null)
                {
                    ThreadMapResult mapped = FeedMapper.FlattenThreadV2(v2.Result.Thread);
                    if (mapped.Rows.Count > 0)
                    {
                        return new ThreadLoadResult(
                            mapped.Rows, UsedV2: true,
                            HasHiddenReplies: v2.Result.HasOtherReplies || mapped.HiddenCount > 0,
                            v2.StatusCode);
                    }

                    // The anchor came back blocked / not found: v1 would say the same, so stop here.
                    return new ThreadLoadResult([], UsedV2: true, HasHiddenReplies: false, HttpStatusCode.NotFound);
                }

                LogService.Warn("Thread", $"GetPostThreadV2 failed: {(int)v2.StatusCode} {v2.AtErrorDetail?.Error}; falling back to v1");
                if (v2.StatusCode == HttpStatusCode.NotFound)
                    return new ThreadLoadResult([], UsedV2: true, HasHiddenReplies: false, HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                LogService.Error("Thread", "GetPostThreadV2 threw; falling back to v1", ex);
            }
        }

        return await LoadV1Async(agent, atUri);
    }

    /// <summary>Replies the thread response left out (hidden by the author, muted), shown on demand.</summary>
    public static async Task<List<ThreadRow>?> LoadOtherRepliesAsync(string atUri, string? anchorHandle)
    {
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            AtProtoHttpResult<IReadOnlyCollection<ThreadItem>> result =
                await BlueskySessionService.Instance.Agent.GetPostThreadOtherV2(
                    new AtUri(atUri), LabelService.Instance.Subscribed);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Thread", $"GetPostThreadOtherV2 failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return null;
            }

            return FeedMapper.FlattenOtherReplies(result.Result, anchorHandle);
        }
        catch (Exception ex)
        {
            LogService.Error("Thread", "GetPostThreadOtherV2 threw", ex);
            return null;
        }
    }

    private static async Task<ThreadLoadResult> LoadV1Async(idunno.Bluesky.BlueskyAgent agent, string atUri)
    {
        AtProtoHttpResult<PostThread> result = await agent.GetPostThread(
            new AtUri(atUri),
            depth: V1ReplyDepth,
            parentHeight: 10,
            subscribedLabelers: LabelService.Instance.Subscribed);

        if (!result.Succeeded || result.Result is null)
        {
            LogService.Warn("Thread", $"GetPostThread failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            return new ThreadLoadResult([], UsedV2: false, HasHiddenReplies: false, result.StatusCode);
        }

        return new ThreadLoadResult(
            FeedMapper.FlattenThread(result.Result, V1ReplyDepth), UsedV2: false, HasHiddenReplies: false, result.StatusCode);
    }
}

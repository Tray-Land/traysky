using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Video;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>What <see cref="VideoUploadService.UploadAsync"/> produced.</summary>
/// <param name="Job">The processing job to poll with <c>getJobStatus</c>, when the upload went through.</param>
/// <param name="UseSingleRequest">The multipart route isn't usable here; the caller should use <c>uploadVideo</c> instead.</param>
/// <param name="Error">User-facing reason the upload failed (when neither of the above).</param>
public sealed record VideoUploadOutcome(JobStatus? Job, bool UseSingleRequest, string? Error);

/// <summary>
/// Multipart video upload (<c>startUpload</c> → <c>uploadPart</c>… → <c>finishUpload</c>) with
/// per-part progress, retry of a flaky part via <c>getUploadStatus</c>, and <c>abortUpload</c> on
/// cancel so the server releases its quota reservation. Any problem that looks like "the multipart
/// endpoints aren't available or don't agree with us" reports <see cref="VideoUploadOutcome.UseSingleRequest"/>
/// so the compose box keeps working through the older single-request path.
/// </summary>
public static class VideoUploadService
{
    private const int MaxRounds = 3;

    public static async Task<AtProtoHttpResult<UploadLimits>?> GetLimitsAsync(BlueskyAgent agent, CancellationToken ct = default)
    {
        try
        {
            return await agent.GetUploadLimits(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogService.Warn("VideoUpload", $"GetUploadLimits threw: {ex.Message}");
            return null;
        }
    }

    public static async Task<VideoUploadOutcome> UploadAsync(
        BlueskyAgent agent,
        string fileName,
        byte[] bytes,
        string mimeType,
        IProgress<double> progress,
        CancellationToken ct)
    {
        string? jobId = null;
        try
        {
            AtProtoHttpResult<StartUploadResponse> start = await agent.StartUpload(bytes.LongLength, mimeType, fileName, cancellationToken: ct);
            if (!start.Succeeded || start.Result is null)
            {
                LogService.Warn("VideoUpload", $"StartUpload failed: {(int)start.StatusCode} {start.AtErrorDetail?.Error}; using single request");
                return new VideoUploadOutcome(null, true, null);
            }

            jobId = start.Result.JobId;
            IReadOnlyList<VideoPart>? parts = VideoUploadPolicy.Split(bytes.LongLength, start.Result.PartSize, start.Result.PartCount);
            if (parts is null)
            {
                LogService.Warn("VideoUpload", $"Part layout {start.Result.PartCount}×{start.Result.PartSize} doesn't fit {bytes.Length} bytes; using single request");
                await AbortQuietlyAsync(agent, jobId);
                return new VideoUploadOutcome(null, true, null);
            }

            var sent = new HashSet<long>();
            progress.Report(0);

            for (int round = 0; round < MaxRounds; round++)
            {
                bool failed = false;
                foreach (VideoPart part in parts.Where(p => !sent.Contains(p.Number)))
                {
                    var slice = new byte[part.Length];
                    Buffer.BlockCopy(bytes, part.Offset, slice, 0, part.Length);

                    AtProtoHttpResult<UploadPartResponse> result = await agent.UploadPart(jobId, part.Number, slice, cancellationToken: ct);
                    if (!result.Succeeded)
                    {
                        LogService.Warn("VideoUpload", $"UploadPart {part.Number} failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                        failed = true;
                        break;
                    }

                    sent.Add(part.Number);
                    progress.Report(VideoUploadPolicy.Fraction(sent.Sum(n => parts[(int)(n - VideoUploadPolicy.FirstPartNumber)].Length), bytes.LongLength));
                }

                if (!failed)
                    break;

                // Nothing accepted on the first try suggests the numbering or endpoint isn't what we expect.
                if (sent.Count == 0 && round == 0)
                {
                    await AbortQuietlyAsync(agent, jobId);
                    return new VideoUploadOutcome(null, true, null);
                }

                // Ask the server what it has, so a retry only resends what's missing.
                AtProtoHttpResult<UploadStatus> status = await agent.GetUploadStatus(jobId, ct);
                if (!status.Succeeded || status.Result is null || status.Result.State != UploadState.Created)
                {
                    await AbortQuietlyAsync(agent, jobId);
                    return new VideoUploadOutcome(null, false, "Couldn't upload the video. Try again.");
                }

                foreach (long received in status.Result.ReceivedParts)
                    sent.Add(received);

                if (round == MaxRounds - 1)
                {
                    await AbortQuietlyAsync(agent, jobId);
                    return new VideoUploadOutcome(null, false, "Couldn't upload the video. Try again.");
                }
            }

            progress.Report(1);
            AtProtoHttpResult<FinishUploadResponse> finish = await agent.FinishUpload(jobId, ct);
            if (!finish.Succeeded || finish.Result is null)
            {
                LogService.Warn("VideoUpload", $"FinishUpload failed: {(int)finish.StatusCode} {finish.AtErrorDetail?.Error}");
                await AbortQuietlyAsync(agent, jobId);
                return new VideoUploadOutcome(null, false, "Couldn't finish the video upload. Try again.");
            }

            return new VideoUploadOutcome(finish.Result.JobStatus, false, null);
        }
        catch (OperationCanceledException)
        {
            if (jobId is not null)
                await AbortQuietlyAsync(agent, jobId);
            throw;
        }
    }

    /// <summary>Best effort: releases the server's reservation; a failure here must never mask the real outcome.</summary>
    private static async Task AbortQuietlyAsync(BlueskyAgent agent, string jobId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            AtProtoHttpResult<AbortUploadResponse> result = await agent.AbortUpload(jobId, timeout.Token);
            if (!result.Succeeded)
                LogService.Warn("VideoUpload", $"AbortUpload failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("VideoUpload", $"AbortUpload threw: {ex.Message}");
        }
    }
}

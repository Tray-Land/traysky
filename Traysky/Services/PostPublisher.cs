using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using idunno.Bluesky.Embed;
using idunno.Bluesky.Feed;
using idunno.Bluesky.Video;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>One picked image or video ready to upload: what the compose box and a saved thread both hold.</summary>
public sealed record MediaUpload(byte[] Bytes, string MimeType, string FileName, bool IsVideo, string AltText, int? PixelWidth, int? PixelHeight)
{
    public bool HasAspectRatio => PixelWidth is > 0 && PixelHeight is > 0;

    public AspectRatio? AspectRatio => HasAspectRatio ? new AspectRatio(PixelWidth!.Value, PixelHeight!.Value) : null;
}

/// <summary>A post's media once uploaded: blobs ready to embed.</summary>
public sealed record PreparedMedia(List<EmbeddedImage>? Images, EmbeddedVideo? Video)
{
    public static PreparedMedia None { get; } = new(null, null);

    public bool HasMedia => Images is { Count: > 0 } || Video is not null;
}

/// <summary>Uploaded media, or why it couldn't be uploaded.</summary>
public readonly record struct MediaResult(PreparedMedia? Media, string? Error);

/// <summary>How a video upload is going, for a view that shows it. Every member is optional.</summary>
public sealed class UploadReporter
{
    /// <summary>Upload progress 0–1, or null when it can't be measured (the single-request fallback).</summary>
    public Action<double?>? Progress { get; init; }

    /// <summary>The video is up and Bluesky has started processing it.</summary>
    public Action? ProcessingStarted { get; init; }
}

/// <summary>
/// The steps that put one post on Bluesky, shared by the compose box and the scheduler so both send
/// exactly the same record: uploads first (nothing is created until every blob is up), then a
/// <see cref="PostBuilder"/> with languages, content warning, media or link card, reply and quote, and
/// hashtag-aware facets. Callers own the order of a thread's posts and what to do when one fails.
/// </summary>
public static class PostPublisher
{
    /// <summary>Uploads one post's images, or its video (waiting for processing). Empty media gives <see cref="PreparedMedia.None"/>.</summary>
    public static async Task<MediaResult> PrepareMediaAsync(BlueskyAgent agent, IReadOnlyList<MediaUpload> media, UploadReporter? reporter, CancellationToken ct)
    {
        if (media.Count == 0)
            return new MediaResult(PreparedMedia.None, null);

        MediaUpload? video = media.FirstOrDefault(m => m.IsVideo);
        if (video is not null)
        {
            (EmbeddedVideo? embedded, string? error) = await UploadVideoAsync(agent, video, reporter, ct);
            return embedded is null ? new MediaResult(null, error) : new MediaResult(new PreparedMedia(null, embedded), null);
        }

        var images = new List<EmbeddedImage>(media.Count);
        foreach (MediaUpload image in media)
        {
            var upload = await agent.UploadBlob(image.Bytes, image.MimeType, cancellationToken: ct);
            if (!upload.Succeeded || upload.Result is null)
                return new MediaResult(null, "Couldn't upload an image. Try again.");

            images.Add(new EmbeddedImage(upload.Result, image.AltText, image.AspectRatio));
        }

        return new MediaResult(new PreparedMedia(images, null), null);
    }

    /// <summary>
    /// Uploads and waits for a video to finish processing. Bluesky's video pipeline is asynchronous
    /// (transcoding happens server-side), so unlike an image blob this needs a poll loop.
    /// </summary>
    private static async Task<(EmbeddedVideo? Video, string? Error)> UploadVideoAsync(BlueskyAgent agent, MediaUpload video, UploadReporter? reporter, CancellationToken ct)
    {
        reporter?.Progress?.Invoke(0);
        VideoUploadOutcome outcome = await VideoUploadService.UploadAsync(
            agent, video.FileName, video.Bytes, video.MimeType, new Progress<double>(p => reporter?.Progress?.Invoke(p)), ct);

        JobStatus? uploaded = outcome.Job;
        if (outcome.UseSingleRequest)
        {
            // The multipart route isn't usable; fall back to the original single request (no progress).
            reporter?.Progress?.Invoke(null);
            AtProtoHttpResult<JobStatus> single = await agent.UploadVideo(video.FileName, video.Bytes, video.MimeType, ct);
            uploaded = single.Succeeded ? single.Result : null;
        }

        if (uploaded is null)
            return (null, outcome.Error ?? "Couldn't upload the video. Try again.");

        reporter?.Progress?.Invoke(null);
        reporter?.ProcessingStarted?.Invoke();
        JobStatus job = uploaded;

        // The transcoding job is polled rather than pushed; the budget is bounded because failing
        // loudly beats spinning forever.
        for (int attempt = 0; job.State is not (JobState.Completed or JobState.Failed) && !UploadProgressPolicy.IsPollBudgetSpent(attempt); attempt++)
        {
            await Task.Delay(UploadProgressPolicy.PollInterval, ct);
            AtProtoHttpResult<JobStatus> status = await agent.GetJobStatus(job.JobId, ct);
            if (!status.Succeeded || status.Result is null)
                return (null, "Couldn't check video processing status.");

            job = status.Result;
        }

        if (job.State != JobState.Completed || job.Blob is null)
            return (null, string.IsNullOrEmpty(job.Message) ? "Video processing failed." : job.Message);

        return (new EmbeddedVideo(job.Blob, captions: null, video.AltText, video.AspectRatio), null);
    }

    /// <summary>
    /// Creates one post. <paramref name="label"/> applies only when there is media; the link card only
    /// when there's no media; <paramref name="replyTo"/> makes it a reply (the root is looked up from it).
    /// </summary>
    public static async Task<AtProtoHttpResult<CreateRecordResult>> PostAsync(
        BlueskyAgent agent,
        string text,
        string[] langs,
        ComposeContentLabel label,
        PreparedMedia media,
        EmbeddedExternal? linkCard = null,
        StrongReference? replyTo = null,
        StrongReference? quote = null)
    {
        // Always a builder, never agent.Post(text): the library's own extractor would end a tag at a hyphen.
        var builder = new PostBuilder(text, langs: langs);

        string? labelValue = ContentLabelPolicy.Value(ContentLabelPolicy.Effective(label, media.HasMedia));
        if (labelValue is not null)
            builder.Add(new idunno.AtProto.Labels.SelfLabel(labelValue));

        if (!AddMedia(builder, media) && linkCard is not null)
            builder.EmbedRecord(linkCard);

        if (replyTo is not null)
            builder = await builder.ReplyTo(replyTo, agent);
        if (quote is not null)
            builder.Add(quote);

        await builder.ExtractFacets(new HashtagAwareFacetExtractor(agent.FacetExtractor), CancellationToken.None);
        return await agent.Post(builder);
    }

    /// <summary>A link card for the first link in <paramref name="text"/>, or null when there's none, it can't be made, or <paramref name="ct"/> runs out first.</summary>
    public static async Task<EmbeddedExternal?> TryLinkCardAsync(BlueskyAgent agent, string text, CancellationToken ct)
    {
        if (LinkCardPolicy.FirstLink(text) is not { } uri)
            return null;

        try
        {
            return await agent.CreateOpenGraphEmbeddedCardGenerator().Generate(uri, ct);
        }
        catch (Exception ex)
        {
            LogService.Warn("Publish", $"Link preview for '{uri.Host}' failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>What to tell the user when Bluesky refused a post.</summary>
    public static string FailureText(System.Net.HttpStatusCode status, string? detail) => status switch
    {
        System.Net.HttpStatusCode.TooManyRequests => "Bluesky said to slow down.",
        0 => "Couldn't reach Bluesky.",
        _ => string.IsNullOrEmpty(detail) ? "Bluesky didn't accept the post." : detail.TrimEnd('.') + "."
    };

    /// <summary>Embeds uploaded media in a post; false when there was none.</summary>
    private static bool AddMedia(PostBuilder builder, PreparedMedia media)
    {
        if (media.Video is not null)
            builder.Add(media.Video);
        else if (media.Images is { Count: > 0 } images)
            builder.Add((ICollection<EmbeddedImage>)images);
        else
            return false;

        return true;
    }
}

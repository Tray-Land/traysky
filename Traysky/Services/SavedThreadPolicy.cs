using System;
using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>Where a saved thread stands: a plain draft, waiting for its time, or past it.</summary>
public enum SavedThreadState
{
    Draft,
    Scheduled,
    Overdue
}

/// <summary>Decisions about saved threads: what's worth saving, how to describe one, and (for scheduling) whether it is due. Pure .NET.</summary>
public static class SavedThreadPolicy
{
    public static bool HasContent(SavedThread thread) =>
        !string.IsNullOrEmpty(thread.QuoteUri)
        || thread.Posts.Any(p => !string.IsNullOrWhiteSpace(p.Text) || p.Media.Count > 0);

    public static int MediaCount(SavedThread thread) => thread.Posts.Sum(p => p.Media.Count);

    public static bool HasMedia(SavedThread thread) => MediaCount(thread) > 0;

    public static SavedThreadState StateAt(SavedThread thread, DateTimeOffset now) => thread.ScheduledAt switch
    {
        null => SavedThreadState.Draft,
        { } at when at > now => SavedThreadState.Scheduled,
        _ => SavedThreadState.Overdue
    };

    /// <summary>
    /// Scheduled threads whose time has passed, soonest first: what the scheduler looks at. Whether each
    /// is posted or waits for the user (<see cref="SchedulePolicy.Missed"/>) is <see cref="SchedulePolicy.DueAt"/>'s call.
    /// </summary>
    public static IReadOnlyList<SavedThread> Overdue(IEnumerable<SavedThread> threads, DateTimeOffset now) =>
        threads.Where(t => StateAt(t, now) == SavedThreadState.Overdue).OrderBy(t => t.ScheduledAt).ToList();

    /// <summary>The first post's text on one line, as <see cref="DraftPolicy.Preview"/> does for a single post.</summary>
    public static string Preview(SavedThread thread)
    {
        string first = thread.Posts.FirstOrDefault()?.Text ?? string.Empty;
        string preview = DraftPolicy.Preview(first, !string.IsNullOrEmpty(thread.QuoteUri));
        return preview == "(empty draft)" && HasMedia(thread) ? "(media only)" : preview;
    }

    /// <summary>"3 posts · 2 images · video", or null for a single text post with nothing extra.</summary>
    public static string? Summary(SavedThread thread)
    {
        List<string> parts = [];
        if (thread.Posts.Count > 1)
            parts.Add($"{thread.Posts.Count} posts");

        int images = thread.Posts.Sum(p => p.Media.Count(m => !m.IsVideo));
        int videos = thread.Posts.Sum(p => p.Media.Count(m => m.IsVideo));
        if (images > 0)
            parts.Add(images == 1 ? "1 image" : $"{images} images");
        if (videos > 0)
            parts.Add(videos == 1 ? "1 video" : $"{videos} videos");

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The same description for a thread that only exists on Bluesky (text posts, no media).</summary>
    public static string? RemoteSummary(int postCount) => postCount > 1 ? $"{postCount} posts" : null;

    /// <summary>What a save leaves out of the Bluesky copy, said after saving. Null when nothing is lost.</summary>
    public static string? MirrorNote(bool hasMedia, bool isReply)
    {
        string? media = hasMedia ? "Images and video are kept on this PC only; bsky.app drafts get the text." : null;
        string? reply = isReply ? "The post you're replying to isn't saved with it." : null;
        return (media, reply) switch
        {
            (null, null) => null,
            (not null, null) => media,
            (null, not null) => reply,
            _ => $"{media} {reply}"
        };
    }

    /// <summary>Records who may reply on the thread (the reply controls belong to the first post).</summary>
    public static void SetReply(SavedThread thread, ReplyChoice choice)
    {
        thread.ReplyAudience = choice.Audience.ToString();
        thread.ReplyMentioned = choice.Mentioned;
        thread.ReplyFollowers = choice.Followers;
        thread.ReplyFollowing = choice.Following;
        thread.ReplyListUris = [.. choice.Lists];
    }

    public static ReplyChoice ReplyOf(SavedThread thread) =>
        Enum.TryParse(thread.ReplyAudience, out ReplyAudience audience)
            ? new ReplyChoice(audience, thread.ReplyMentioned, thread.ReplyFollowers, thread.ReplyFollowing, thread.ReplyListUris)
            : ReplyChoice.Everyone;

    /// <summary>The content warning a saved post carries, or none if the name is unknown.</summary>
    public static ComposeContentLabel LabelOf(SavedPost post) =>
        Enum.TryParse(post.Label, out ComposeContentLabel label) ? label : ComposeContentLabel.None;

    /// <summary>A file name for stored media bytes: stable per position, so re-saving overwrites rather than piling up.</summary>
    public static string MediaFileName(int postIndex, int mediaIndex) => $"p{postIndex}m{mediaIndex}.bin";
}

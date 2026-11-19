using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Traysky.Services;

/// <summary>
/// A whole thread kept on this PC: every post with its text, media and content warning, plus the
/// reply / quote controls and languages. The unit that saving a draft stores, and that scheduling
/// stores with a <see cref="ScheduledAt"/>. Pure .NET (no WinRT) so it round-trips in tests.
/// </summary>
public sealed class SavedThread
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the thread should be posted; null for a plain draft. Scheduling is built on this (see <see cref="SchedulePolicy"/>).</summary>
    public DateTimeOffset? ScheduledAt { get; set; }

    /// <summary>The account it was written on. A scheduled thread is only ever posted from this account.</summary>
    public string? AccountDid { get; set; }

    /// <summary>That account's handle when it was saved, for saying which account to sign in to.</summary>
    public string? AccountHandle { get; set; }

    /// <summary>How many of <see cref="Posts"/> are already on Bluesky: a scheduled thread that was cut off resumes from here instead of posting the start again.</summary>
    public int SentCount { get; set; }

    /// <summary>The last post that went out, which the next one replies to.</summary>
    public string? LastSentUri { get; set; }

    public string? LastSentCid { get; set; }

    /// <summary>Tries at posting it on schedule since it was last scheduled; see <see cref="SchedulePolicy.MaxAttempts"/>.</summary>
    public int Attempts { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>Why the last try at posting it failed, said in the drafts list.</summary>
    public string? LastError { get; set; }

    public List<SavedPost> Posts { get; set; } = [];

    public List<string> Langs { get; set; } = [];

    /// <summary>The quoted post's at:// uri; the post is looked up again on restore.</summary>
    public string? QuoteUri { get; set; }

    /// <summary>The quoted post's cid, so a scheduled quote can be posted without looking the post up again.</summary>
    public string? QuoteCid { get; set; }

    /// <summary>False when the link preview was removed; otherwise a scheduled post gets a card for its first link, as the compose box would.</summary>
    public bool LinkCard { get; set; } = true;

    /// <summary>"Everyone", "Nobody" or "Limited", with the groups let in for the last.</summary>
    public string ReplyAudience { get; set; } = "Everyone";

    public bool ReplyMentioned { get; set; }

    public bool ReplyFollowers { get; set; }

    public bool ReplyFollowing { get; set; }

    public List<string> ReplyListUris { get; set; } = [];

    public bool AllowQuotes { get; set; } = true;

    /// <summary>The id of the text-only copy mirrored to Bluesky drafts, so it is updated rather than duplicated.</summary>
    public string? BlueskyDraftId { get; set; }
}

public sealed class SavedPost
{
    public string Text { get; set; } = string.Empty;

    /// <summary>A <see cref="ComposeContentLabel"/> name; "None" for no content warning.</summary>
    public string Label { get; set; } = nameof(ComposeContentLabel.None);

    public List<SavedMedia> Media { get; set; } = [];
}

public sealed class SavedMedia
{
    public string FileName { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public bool IsVideo { get; set; }

    public string AltText { get; set; } = string.Empty;

    public int? PixelWidth { get; set; }

    public int? PixelHeight { get; set; }

    /// <summary>The file inside the thread's folder holding the bytes; set by the store when saving.</summary>
    public string StoredAs { get; set; } = string.Empty;

    /// <summary>The bytes, in memory only: given to the store to write, filled in when it loads them.</summary>
    [JsonIgnore]
    public byte[]? Bytes { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SavedThread))]
public sealed partial class SavedThreadJsonContext : JsonSerializerContext
{
}

using idunno.AtProto;
using idunno.AtProto.Repo;
using System.Collections.Generic;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One saved draft: either a thread kept on this PC (<see cref="Local"/>, with media), or one that exists
/// only on Bluesky (projected from <c>DraftView</c>, see <see cref="DraftsService"/>). A local thread
/// that was mirrored to Bluesky shows once, with <see cref="Id"/> pointing at its mirror.
/// </summary>
public sealed class DraftItem
{
    /// <summary>The draft's server-side id; null for a thread that hasn't been (or couldn't be) mirrored, and for the preview account's samples.</summary>
    public TimestampIdentifier? Id { get; init; }

    /// <summary>The thread as kept on this PC, when there is one. Restoring prefers it: it has the media.</summary>
    public SavedThread? Local { get; init; }

    public string? LocalId => Local?.Id;

    public string Text { get; init; } = string.Empty;

    /// <summary>The texts of the posts after the first, for a thread that exists only on Bluesky.</summary>
    public IReadOnlyList<string> ExtraTexts { get; init; } = [];

    /// <summary>The post being quoted, if the draft was a quote and exists only on Bluesky.</summary>
    public StrongReference? Quote { get; init; }

    public ReplyChoice Reply { get; init; } = ReplyChoice.Everyone;

    public bool AllowQuotes { get; init; } = true;

    /// <summary>"3m ago": when it was last saved.</summary>
    public string Age { get; init; } = string.Empty;

    /// <summary>When it was last saved, for ordering.</summary>
    public System.DateTimeOffset SortKey { get; init; }

    /// <summary>Set when the draft had media that can't be restored here.</summary>
    public string? Note { get; init; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// <summary>"3 posts · 2 images": what's in it beyond the first post's text; null for a plain single post.</summary>
    public string? Summary { get; init; }

    public bool HasSummary => !string.IsNullOrEmpty(Summary);

    /// <summary>"Scheduled for today at 3:00 PM", "Didn't post · was due …"; null for a plain draft.</summary>
    public string? ScheduleText { get; init; }

    public bool HasScheduleText => !string.IsNullOrEmpty(ScheduleText);

    /// <summary>A scheduled thread waiting for the user (missed, or out of tries): shown in the warning color.</summary>
    public bool NeedsAttention { get; init; }

    public string ScheduleBrushKey => NeedsAttention ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush";

    /// <summary>Scheduled threads can be sent straight from the list, which also finishes one that was cut off.</summary>
    public bool CanPostNow => Local?.ScheduledAt is not null;

    /// <summary>Its place in the list (see <see cref="SchedulePolicy.SortGroup"/>), then <see cref="ScheduledAt"/> within the scheduled ones.</summary>
    public int SortGroup { get; init; } = 2;

    public System.DateTimeOffset? ScheduledAt => Local?.ScheduledAt;

    public string Preview => Local is not null
        ? SavedThreadPolicy.Preview(Local)
        : DraftPolicy.Preview(Text, Quote is not null);

    public bool HasQuote => Quote is not null || !string.IsNullOrEmpty(Local?.QuoteUri);
}

using idunno.AtProto;
using idunno.AtProto.Repo;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>One saved draft, projected from <c>DraftView</c> (see <see cref="DraftsService"/>).</summary>
public sealed class DraftItem
{
    /// <summary>The draft's server-side id; null only for the preview account's samples.</summary>
    public TimestampIdentifier? Id { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>The post being quoted, if the draft was a quote.</summary>
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

    public string Preview => DraftPolicy.Preview(Text, Quote is not null);

    public bool HasQuote => Quote is not null;
}

using CommunityToolkit.Mvvm.ComponentModel;
using idunno.AtProto.Repo;
using System;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One post as the timeline, a thread or a notification shows it. Projected once from the
/// library's <c>PostView</c> so the XAML binds to plain properties, and observable for the
/// handful of things the user can change from the card (like / repost state and counts).
/// </summary>
public sealed partial class PostItem : ObservableObject
{
    public string AtUri { get; init; } = string.Empty;
    public string Cid { get; init; } = string.Empty;
    public StrongReference Reference { get; init; } = null!;

    public string AuthorDid { get; init; } = string.Empty;
    public string AuthorHandle { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public Uri? AvatarUri { get; init; }

    /// <summary>Community labels on the author's account, alerts first; empty for most people.</summary>
    public IReadOnlyList<LabelDisplay> AuthorLabels { get; init; } = [];

    public bool HasAuthorLabels => AuthorLabels.Count > 0;

    public bool HasAlertLabel => AuthorLabels.Any(l => l.IsAlert);

    public string AuthorLabelsToolTip => AuthorLabels.Count == 1 ? "1 label on this account" : $"{AuthorLabels.Count} labels on this account";

    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<TextSegment> Segments { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>"♲ Reposted by Sam" when the timeline shows this because someone reposted it.</summary>
    public string? RepostedBy { get; init; }

    /// <summary>The handle this post replies to, when it is a reply.</summary>
    public string? ReplyingTo { get; init; }

    /// <summary>
    /// Timeline reply context: a connector line is drawn from the top of the card down to the
    /// avatar (this post replies to the post above it) and/or from the avatar down to the bottom
    /// (the post below replies to this one). Set before the row is shown; not observable.
    /// </summary>
    public bool ThreadLineAbove { get; set; }

    /// <inheritdoc cref="ThreadLineAbove"/>
    public bool ThreadLineBelow { get; set; }

    /// <summary>The card's bottom divider is left out where a connector line runs into the next row.</summary>
    public bool ShowDivider => !ThreadLineBelow;

    public EmbedItem? Embed { get; init; }

    public string? WebUrl { get; init; }

    [ObservableProperty]
    public partial int LikeCount { get; set; }

    [ObservableProperty]
    public partial int RepostCount { get; set; }

    [ObservableProperty]
    public partial int ReplyCount { get; set; }

    [ObservableProperty]
    public partial int QuoteCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeGlyph))]
    public partial bool IsLiked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepostGlyph))]
    public partial bool IsReposted { get; set; }

    /// <summary>AT URI of the viewer's like record; needed to undo the like.</summary>
    public string? LikeUri { get; set; }

    /// <summary>AT URI of the viewer's repost record; needed to undo the repost.</summary>
    public string? RepostUri { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BookmarkLabel))]
    public partial bool IsBookmarked { get; set; }

    /// <summary>AT URI of the thread's root post (this post when it is not a reply); what mute-thread acts on.</summary>
    public string ThreadRootUri { get; init; } = string.Empty;

    [ObservableProperty]
    public partial bool IsThreadMuted { get; set; }

    /// <summary>Set while a like/repost/bookmark request is in flight so a double-click cannot send two.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string TimeAgo => RelativeTimeFormatter.Format(CreatedAt, DateTimeOffset.UtcNow);

    public string AbsoluteTime => RelativeTimeFormatter.FormatAbsolute(CreatedAt);

    public string HandleDisplay => "@" + AuthorHandle;

    public bool HasRepostedBy => !string.IsNullOrEmpty(RepostedBy);

    // The post replied to is right above, joined by a line, so the label would only repeat it.
    public bool HasReplyingTo => !string.IsNullOrEmpty(ReplyingTo) && !ThreadLineAbove;

    public string ReplyingToDisplay => HasReplyingTo ? $"Replying to @{ReplyingTo}" : string.Empty;

    public bool HasEmbed => Embed is not null && Embed.Kind != EmbedKind.None;

    public bool HasText => !string.IsNullOrEmpty(Text);

    // Segoe Fluent Icons: filled heart / outline heart, and the repost arrows.
    public string LikeGlyph => IsLiked ? "" : "";

    public string RepostGlyph => "";

    public string BookmarkLabel => IsBookmarked ? "Remove bookmark" : "Bookmark";

    public string LikeCountDisplay => LikeCount > 0 ? LikeCount.ToString() : string.Empty;
    public string RepostCountDisplay => RepostCount > 0 ? RepostCount.ToString() : string.Empty;
    public string ReplyCountDisplay => ReplyCount > 0 ? ReplyCount.ToString() : string.Empty;

    partial void OnLikeCountChanged(int value) => OnPropertyChanged(nameof(LikeCountDisplay));

    partial void OnRepostCountChanged(int value) => OnPropertyChanged(nameof(RepostCountDisplay));

    public string QuoteCountDisplay => QuoteCount > 0 ? QuoteCount.ToString() : string.Empty;

    partial void OnQuoteCountChanged(int value) => OnPropertyChanged(nameof(QuoteCountDisplay));

    partial void OnReplyCountChanged(int value) => OnPropertyChanged(nameof(ReplyCountDisplay));

    /// <summary>Re-raises the relative timestamp so a list that has been open a while stays honest.</summary>
    public void RefreshTimeAgo() => OnPropertyChanged(nameof(TimeAgo));
}

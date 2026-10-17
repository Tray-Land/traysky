using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>One trending topic on the Explore page.</summary>
public sealed class TrendItem
{
    public string Topic { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>"Politics · 1,204 posts" style line; either part may be missing.</summary>
    public string Detail { get; init; } = string.Empty;

    public bool HasDetail => Detail.Length > 0;
}

/// <summary>One feed generator row: an Explore suggestion or an entry on a profile's Feeds tab.</summary>
public sealed partial class FeedSuggestionItem : ObservableObject
{
    /// <summary>The generator's AT URI, which is what the saved-feeds preference stores.</summary>
    public string AtUri { get; init; } = string.Empty;

    /// <summary>Whether the feed is one of the account's pinned tabs; the row's button flips it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinLabel))]
    public partial bool IsPinned { get; set; }

    public string PinLabel => SavedFeedPolicy.PinLabel(IsPinned);

    public bool CanPin => AtUri.Length > 0;

    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CreatorHandle { get; init; } = string.Empty;
    public Uri? AvatarUri { get; init; }
    public int LikeCount { get; init; }
    public string WebUrl { get; init; } = string.Empty;

    public bool HasDescription => Description.Length > 0;

    public string ByLine => LikeCount > 0
        ? $"by @{CreatorHandle} · {LikeCount:N0} likes"
        : $"by @{CreatorHandle}";
}

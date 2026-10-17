using System;

namespace Traysky.ViewModels.Items;

/// <summary>One starter pack in the search results.</summary>
public sealed class StarterPackItem
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CreatorHandle { get; init; } = string.Empty;
    public Uri? CreatorAvatarUri { get; init; }

    /// <summary>The pack's <c>app.bsky.graph.starterpack</c> AT URI; its web link is built from it.</summary>
    public string AtUri { get; init; } = string.Empty;

    /// <summary>The AT URI of the list holding the pack's members.</summary>
    public string ListUri { get; init; } = string.Empty;

    public int MemberCount { get; init; }
    public int JoinedCount { get; init; }

    public string WebUrl { get; init; } = string.Empty;

    public bool HasDescription => Description.Length > 0;

    public string ByLine => MemberCount > 0
        ? $"by @{CreatorHandle} · {MemberCount:N0} {(MemberCount == 1 ? "account" : "accounts")}"
        : $"by @{CreatorHandle}";
}

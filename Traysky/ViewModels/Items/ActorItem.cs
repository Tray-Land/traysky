using System;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One account in a people list (e.g. who liked or reposted a post), projected once from
/// <c>ProfileViewBasic</c> (see <see cref="Services.FeedMapper.ToActorItem"/>).
/// </summary>
public sealed class ActorItem
{
    public string Did { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public Uri? AvatarUri { get; init; }

    /// <summary>The viewer's block record for this account, when known (needed to unblock).</summary>
    public string? BlockUri { get; init; }

    /// <summary>The list-item record that puts this account on the list being viewed (needed to remove them).</summary>
    public string? ListItemUri { get; init; }

    /// <summary>The row button on the blocked / muted screen ("Unblock" or "Unmute"), or "Remove" on a list's member row.</summary>
    public string ManagedActionLabel { get; init; } = string.Empty;

    public bool HasManagedAction => ManagedActionLabel.Length > 0;

    /// <summary>A short note under the handle on management screens, e.g. what a bell is set to.</summary>
    public string ManagedDetail { get; init; } = string.Empty;

    public bool HasManagedDetail => ManagedDetail.Length > 0;

    public string NameOrHandle => DisplayName.Length > 0 ? DisplayName : Handle;

    public string HandleDisplay => "@" + Handle;

    /// <summary>What <see cref="Pages.ProfilePage.Open"/> takes: the DID when known, else the handle.</summary>
    public string ProfileKey => Did.Length > 0 ? Did : Handle;
}

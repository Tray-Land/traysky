namespace Traysky.Services;

/// <summary>
/// What the signed-in account can do about a whole list: mute everyone on it, or block everyone
/// on it. Curation lists can only be muted; moderation lists can be muted or blocked; starter-pack
/// reference lists are neither. Pure .NET on purpose.
/// </summary>
public static class MuteTargetPolicy
{
    public static bool CanMute(ListKind kind) => kind is ListKind.Curate or ListKind.Moderation;

    public static bool CanBlock(ListKind kind) => kind == ListKind.Moderation;

    /// <summary>Curation lists are muted through the plain actor-list call; moderation lists through the mod-list one.</summary>
    public static bool UsesActorListCall(ListKind kind) => kind == ListKind.Curate;

    /// <summary>"Mute list" on a moderation list; "Mute members" on a curation list, where muting the list itself would be misleading.</summary>
    public static string MuteLabel(ListKind kind, bool muted) => kind == ListKind.Curate
        ? (muted ? "Unmute members" : "Mute members")
        : ListPolicy.MuteLabel(muted);
}

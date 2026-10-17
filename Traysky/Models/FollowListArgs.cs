namespace Traysky.Models;

/// <summary>Navigation parameter for the followers / following page.</summary>
/// <param name="Actor">DID or handle of the account whose lists are shown.</param>
/// <param name="ShowFollowing">Open on the Following tab instead of Followers.</param>
/// <param name="KnownOnly">Show only the followers the signed-in account also follows (no tabs); <paramref name="FollowersCount"/> is then that overlap.</param>
public sealed record FollowListArgs(string Actor, bool ShowFollowing, int FollowersCount, int FollowsCount, bool KnownOnly = false);

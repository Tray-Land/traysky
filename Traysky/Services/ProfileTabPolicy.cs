namespace Traysky.Services;

/// <summary>The lists on a profile page. Pure .NET on purpose.</summary>
public enum ProfileTab
{
    Posts,
    Likes,
    Feeds,
    StarterPacks,
    Lists
}

public static class ProfileTabPolicy
{
    /// <summary>Likes are private: the API only returns the signed-in account's own, so only that profile offers the tab.</summary>
    public static bool IsAvailable(ProfileTab tab, bool isSelf) => tab != ProfileTab.Likes || isSelf;

    /// <summary>The tab to fall back to when the selected one isn't offered (e.g. Likes on someone else's profile).</summary>
    public static ProfileTab Normalize(ProfileTab tab, bool isSelf) => IsAvailable(tab, isSelf) ? tab : ProfileTab.Posts;

    public static string EmptyText(ProfileTab tab) => tab switch
    {
        ProfileTab.Likes => "No liked posts yet",
        ProfileTab.Feeds => "No feeds",
        ProfileTab.StarterPacks => "No starter packs",
        ProfileTab.Lists => "No lists",
        _ => "No posts yet"
    };
}

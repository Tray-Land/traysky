using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ProfileTabPolicyTests
{
    [TestMethod]
    public void Likes_OnlyOnOwnProfile()
    {
        Assert.IsTrue(ProfileTabPolicy.IsAvailable(ProfileTab.Likes, isSelf: true));
        Assert.IsFalse(ProfileTabPolicy.IsAvailable(ProfileTab.Likes, isSelf: false));
    }

    [TestMethod]
    public void OtherTabs_AlwaysAvailable()
    {
        foreach (ProfileTab tab in new[] { ProfileTab.Posts, ProfileTab.Feeds, ProfileTab.StarterPacks, ProfileTab.Lists })
        {
            Assert.IsTrue(ProfileTabPolicy.IsAvailable(tab, isSelf: false));
            Assert.IsTrue(ProfileTabPolicy.IsAvailable(tab, isSelf: true));
        }
    }

    [TestMethod]
    public void Normalize_FallsBackToPosts()
    {
        Assert.AreEqual(ProfileTab.Posts, ProfileTabPolicy.Normalize(ProfileTab.Likes, isSelf: false));
        Assert.AreEqual(ProfileTab.Likes, ProfileTabPolicy.Normalize(ProfileTab.Likes, isSelf: true));
        Assert.AreEqual(ProfileTab.Feeds, ProfileTabPolicy.Normalize(ProfileTab.Feeds, isSelf: false));
    }

    [TestMethod]
    public void EmptyText_DiffersPerTab()
    {
        Assert.AreEqual("No posts yet", ProfileTabPolicy.EmptyText(ProfileTab.Posts));
        Assert.AreEqual("No liked posts yet", ProfileTabPolicy.EmptyText(ProfileTab.Likes));
        Assert.AreEqual("No feeds", ProfileTabPolicy.EmptyText(ProfileTab.Feeds));
        Assert.AreEqual("No starter packs", ProfileTabPolicy.EmptyText(ProfileTab.StarterPacks));
        Assert.AreEqual("No lists", ProfileTabPolicy.EmptyText(ProfileTab.Lists));
    }
}

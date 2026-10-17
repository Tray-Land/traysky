using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class MuteTargetPolicyTests
{
    [TestMethod]
    public void CanMute_CurationAndModerationOnly()
    {
        Assert.IsTrue(MuteTargetPolicy.CanMute(ListKind.Curate));
        Assert.IsTrue(MuteTargetPolicy.CanMute(ListKind.Moderation));
        Assert.IsFalse(MuteTargetPolicy.CanMute(ListKind.Reference));
        Assert.IsFalse(MuteTargetPolicy.CanMute(ListKind.Unknown));
    }

    [TestMethod]
    public void CanBlock_ModerationOnly()
    {
        Assert.IsFalse(MuteTargetPolicy.CanBlock(ListKind.Curate));
        Assert.IsTrue(MuteTargetPolicy.CanBlock(ListKind.Moderation));
        Assert.IsFalse(MuteTargetPolicy.CanBlock(ListKind.Reference));
        Assert.IsFalse(MuteTargetPolicy.CanBlock(ListKind.Unknown));
    }

    [TestMethod]
    public void BlockImpliesMute()
    {
        foreach (ListKind kind in System.Enum.GetValues<ListKind>())
        {
            if (MuteTargetPolicy.CanBlock(kind))
                Assert.IsTrue(MuteTargetPolicy.CanMute(kind), kind.ToString());
        }
    }

    [TestMethod]
    public void BlockMatchesTheExistingSubscribeRule()
    {
        foreach (ListKind kind in System.Enum.GetValues<ListKind>())
            Assert.AreEqual(ListPolicy.CanSubscribe(kind), MuteTargetPolicy.CanBlock(kind), kind.ToString());
    }

    [TestMethod]
    public void OnlyCurationListsUseTheActorListCall()
    {
        Assert.IsTrue(MuteTargetPolicy.UsesActorListCall(ListKind.Curate));
        Assert.IsFalse(MuteTargetPolicy.UsesActorListCall(ListKind.Moderation));
    }

    [TestMethod]
    public void MuteLabel_CurationListsTalkAboutMembers()
    {
        Assert.AreEqual("Mute members", MuteTargetPolicy.MuteLabel(ListKind.Curate, muted: false));
        Assert.AreEqual("Unmute members", MuteTargetPolicy.MuteLabel(ListKind.Curate, muted: true));
    }

    [TestMethod]
    public void MuteLabel_ModerationListsKeepTheListWording()
    {
        Assert.AreEqual("Mute list", MuteTargetPolicy.MuteLabel(ListKind.Moderation, muted: false));
        Assert.AreEqual("Unmute list", MuteTargetPolicy.MuteLabel(ListKind.Moderation, muted: true));
    }
}

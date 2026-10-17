using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class PostMenuPolicyTests
{
    [TestMethod]
    public void OwnPost_ShowsDelete_HidesAccountModeration()
    {
        PostMenuState s = PostMenuPolicy.For("did:me", "did:me", signedIn: true, threadMuted: false);
        Assert.IsTrue(s.ShowDelete);
        Assert.IsFalse(s.ShowAccountModeration);
        Assert.IsTrue(s.ShowMuteThread);
        Assert.IsTrue(s.ShowInteractionSettings);
    }

    [TestMethod]
    public void OthersPost_HidesDelete_ShowsAccountModeration()
    {
        PostMenuState s = PostMenuPolicy.For("did:them", "did:me", signedIn: true, threadMuted: false);
        Assert.IsFalse(s.ShowDelete);
        Assert.IsFalse(s.ShowInteractionSettings);
        Assert.IsTrue(s.ShowAccountModeration);
        Assert.IsTrue(s.ShowMuteThread);
    }

    [TestMethod]
    public void SignedOut_OffersNothing()
    {
        PostMenuState s = PostMenuPolicy.For("did:me", "did:me", signedIn: false, threadMuted: false);
        Assert.IsFalse(s.ShowDelete);
        Assert.IsFalse(s.ShowAccountModeration);
        Assert.IsFalse(s.ShowMuteThread);
    }

    [TestMethod]
    public void MissingSessionDid_IsNeverOwn()
    {
        Assert.IsFalse(PostMenuPolicy.For("", null, signedIn: true, threadMuted: false).ShowDelete);
        Assert.IsFalse(PostMenuPolicy.For("did:a", "", signedIn: true, threadMuted: false).ShowDelete);
    }

    [TestMethod]
    public void MuteLabel_FlipsWithThreadState()
    {
        Assert.AreEqual("Mute thread", PostMenuPolicy.For("a", "b", true, threadMuted: false).MuteThreadLabel);
        Assert.AreEqual("Unmute thread", PostMenuPolicy.For("a", "b", true, threadMuted: true).MuteThreadLabel);
    }

    [TestMethod]
    public void ThreadRootUri_FallsBackToThePost()
    {
        Assert.AreEqual("at://post", PostMenuPolicy.ThreadRootUri("at://post", null));
        Assert.AreEqual("at://post", PostMenuPolicy.ThreadRootUri("at://post", ""));
        Assert.AreEqual("at://root", PostMenuPolicy.ThreadRootUri("at://post", "at://root"));
    }
}

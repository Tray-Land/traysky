using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ManagedAccountsPolicyTests
{
    [TestMethod]
    public void RestoreIndex_KeepsOriginalSlotWhenStillValid()
    {
        Assert.AreEqual(2, ManagedAccountsPolicy.RestoreIndex(2, 5));
        Assert.AreEqual(0, ManagedAccountsPolicy.RestoreIndex(0, 5));
    }

    [TestMethod]
    public void RestoreIndex_AllowsAppendingAtTheEnd()
    {
        Assert.AreEqual(5, ManagedAccountsPolicy.RestoreIndex(5, 5));
    }

    [TestMethod]
    public void RestoreIndex_ClampsWhenListShrank()
    {
        Assert.AreEqual(1, ManagedAccountsPolicy.RestoreIndex(9, 1));
        Assert.AreEqual(0, ManagedAccountsPolicy.RestoreIndex(3, 0));
    }

    [TestMethod]
    public void RestoreIndex_ClampsNegative()
    {
        Assert.AreEqual(0, ManagedAccountsPolicy.RestoreIndex(-1, 4));
    }

    [TestMethod]
    public void Labels_FollowTheKind()
    {
        Assert.AreEqual("Unblock", ManagedAccountsPolicy.ActionLabel(blocks: true));
        Assert.AreEqual("Unmute", ManagedAccountsPolicy.ActionLabel(blocks: false));
        Assert.AreEqual("You haven't blocked anyone", ManagedAccountsPolicy.EmptyText(blocks: true));
        Assert.AreEqual("You haven't muted anyone", ManagedAccountsPolicy.EmptyText(blocks: false));
        Assert.AreEqual("Couldn't unblock @a.test. Try again.", ManagedAccountsPolicy.FailureText(true, "a.test"));
        Assert.AreEqual("Couldn't unmute @a.test. Try again.", ManagedAccountsPolicy.FailureText(false, "a.test"));
    }
}

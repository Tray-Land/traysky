using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ChatReadPolicyTests
{
    [TestMethod]
    public void MarkAllRead_NeedsSomethingUnread()
    {
        Assert.IsFalse(ChatReadPolicy.CanMarkAllRead(ChatListMode.All, 0, 0));
        Assert.IsTrue(ChatReadPolicy.CanMarkAllRead(ChatListMode.All, 3, 0));
        Assert.IsTrue(ChatReadPolicy.CanMarkAllRead(ChatListMode.All, 0, 2));
    }

    [TestMethod]
    public void MarkAllRead_NotOfferedInRequests()
    {
        Assert.IsFalse(ChatReadPolicy.CanMarkAllRead(ChatListMode.Requests, 5, 5));
    }

    [TestMethod]
    public void OnlyTheMainListUpdatesTheBadge()
    {
        Assert.IsTrue(ChatReadPolicy.UpdatesBadge(ChatListMode.All));
        Assert.IsFalse(ChatReadPolicy.UpdatesBadge(ChatListMode.Requests));
    }

    [TestMethod]
    public void EmptyText_DiffersByMode()
    {
        Assert.AreEqual("No message requests.", ChatReadPolicy.EmptyText(ChatListMode.Requests));
        StringAssert.StartsWith(ChatReadPolicy.EmptyText(ChatListMode.All), "No conversations");
    }

    [TestMethod]
    public void RequestsLabel_ShowsCountAndMore()
    {
        Assert.AreEqual("Requests", ChatReadPolicy.RequestsLabel(0, false));
        Assert.AreEqual("Requests", ChatReadPolicy.RequestsLabel(-1, true));
        Assert.AreEqual("Requests (3)", ChatReadPolicy.RequestsLabel(3, false));
        Assert.AreEqual("Requests (30+)", ChatReadPolicy.RequestsLabel(30, true));
    }
}

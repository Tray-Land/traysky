using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ChatNotificationPreferencePolicyTests
{
    [TestMethod]
    public void Rows_CoverChatAndRequests()
    {
        Assert.AreEqual(2, ChatNotificationPreferencePolicy.Rows.Count);
        Assert.AreEqual(ChatPrefType.Chat, ChatNotificationPreferencePolicy.Rows[0].Type);
        Assert.AreEqual(ChatPrefType.ChatRequest, ChatNotificationPreferencePolicy.Rows[1].Type);
    }

    [TestMethod]
    public void Summary_ReadsOffPushedAndFollowsOnly()
    {
        Assert.AreEqual("Off", ChatNotificationPreferencePolicy.Summary(false, false));
        Assert.AreEqual("Off", ChatNotificationPreferencePolicy.Summary(false, true));
        Assert.AreEqual("Pushed, from everyone", ChatNotificationPreferencePolicy.Summary(true, false));
        Assert.AreEqual("Pushed, from people you follow", ChatNotificationPreferencePolicy.Summary(true, true));
    }

    [TestMethod]
    public void Defaults_ArePushedFromEveryone()
    {
        Assert.AreEqual(
            "Pushed, from everyone",
            ChatNotificationPreferencePolicy.Summary(ChatNotificationPreferencePolicy.DefaultPush, ChatNotificationPreferencePolicy.DefaultFollowsOnly));
    }
}

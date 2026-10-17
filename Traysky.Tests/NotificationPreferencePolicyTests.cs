using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class NotificationPreferencePolicyTests
{
    [TestMethod]
    public void Rows_CoverEveryTypeExactlyOnce()
    {
        List<NotificationPrefType> shown = NotificationPreferencePolicy.Rows.Select(r => r.Type).ToList();

        CollectionAssert.AreEquivalent(Enum.GetValues<NotificationPrefType>().ToList(), shown);
    }

    [TestMethod]
    public void Rows_OnlyReactionTypesCanBeLimitedToFollows()
    {
        Assert.IsTrue(NotificationPreferencePolicy.RowFor(NotificationPrefType.Like).CanLimitToFollows);
        Assert.IsTrue(NotificationPreferencePolicy.RowFor(NotificationPrefType.Follow).CanLimitToFollows);
        Assert.IsFalse(NotificationPreferencePolicy.RowFor(NotificationPrefType.SubscribedPost).CanLimitToFollows);
        Assert.IsFalse(NotificationPreferencePolicy.RowFor(NotificationPrefType.StarterPackJoined).CanLimitToFollows);
        Assert.IsFalse(NotificationPreferencePolicy.RowFor(NotificationPrefType.Verified).CanLimitToFollows);
    }

    [TestMethod]
    public void Rows_HaveLabelsAndDescriptions()
    {
        foreach (NotificationPrefRow row in NotificationPreferencePolicy.Rows)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.Label), row.Type.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.Description), row.Type.ToString());
        }
    }

    [TestMethod]
    public void Summary_DescribesEachCombination()
    {
        Assert.AreEqual("Off", NotificationPreferencePolicy.Summary(false, false, false));
        Assert.AreEqual("Off", NotificationPreferencePolicy.Summary(false, false, true));
        Assert.AreEqual("In your list and pushed", NotificationPreferencePolicy.Summary(true, true, false));
        Assert.AreEqual("In your list only", NotificationPreferencePolicy.Summary(true, false, false));
        Assert.AreEqual("Pushed only", NotificationPreferencePolicy.Summary(false, true, false));
        Assert.AreEqual("In your list only, from people you follow", NotificationPreferencePolicy.Summary(true, false, true));
    }
}

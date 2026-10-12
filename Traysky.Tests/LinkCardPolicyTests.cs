using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class LinkCardPolicyTests
{
    [TestMethod]
    public void FirstLink_NoText_IsNull()
    {
        Assert.IsNull(LinkCardPolicy.FirstLink(null));
        Assert.IsNull(LinkCardPolicy.FirstLink(""));
        Assert.IsNull(LinkCardPolicy.FirstLink("no links here"));
    }

    [TestMethod]
    public void FirstLink_FindsLinkInsideText()
    {
        Assert.AreEqual("https://example.com/a?b=1", LinkCardPolicy.FirstLink("look at https://example.com/a?b=1 now")?.AbsoluteUri);
    }

    [TestMethod]
    public void FirstLink_TrimsTrailingPunctuation()
    {
        Assert.AreEqual("https://example.com/page", LinkCardPolicy.FirstLink("see (https://example.com/page).")?.AbsoluteUri);
        Assert.AreEqual("https://example.com/page", LinkCardPolicy.FirstLink("https://example.com/page!")?.AbsoluteUri);
    }

    [TestMethod]
    public void FirstLink_OnlyTheFirstOneCounts()
    {
        Assert.AreEqual("https://one.example/", LinkCardPolicy.FirstLink("https://one.example then https://two.example")?.AbsoluteUri);
    }

    [TestMethod]
    public void FirstLink_IgnoresHostsWithoutADot()
    {
        Assert.IsNull(LinkCardPolicy.FirstLink("http://localhost/thing"));
    }

    [TestMethod]
    public void ThumbnailPreviewUri_NeedsBothParts()
    {
        Assert.IsNull(LinkCardPolicy.ThumbnailPreviewUri(null, "cid"));
        Assert.IsNull(LinkCardPolicy.ThumbnailPreviewUri("did:plc:abc", ""));
        Assert.AreEqual(
            "https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:abc/bafkcid@jpeg",
            LinkCardPolicy.ThumbnailPreviewUri("did:plc:abc", "bafkcid")?.AbsoluteUri);
    }

    [TestMethod]
    public void CanAttachCard_YieldsToOtherEmbeds()
    {
        Assert.IsTrue(LinkCardPolicy.CanAttachCard(false, false));
        Assert.IsFalse(LinkCardPolicy.CanAttachCard(true, false));
        Assert.IsFalse(LinkCardPolicy.CanAttachCard(false, true));
    }
}

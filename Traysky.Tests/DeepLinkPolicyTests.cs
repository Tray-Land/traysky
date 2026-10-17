using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class DeepLinkPolicyTests
{
    private static DeepLink Parse(string uri)
    {
        Assert.IsTrue(DeepLinkPolicy.TryParse(new Uri(uri), out DeepLink? link), uri);
        return link!;
    }

    [TestMethod]
    public void ComposeCarriesTextAndUrl()
    {
        DeepLink link = Parse("traysky://compose?text=Hello%20there&url=https%3A%2F%2Fexample.com%2F");
        Assert.AreEqual(DeepLinkKind.Compose, link.Kind);
        Assert.AreEqual("Hello there\nhttps://example.com/", link.Text);
    }

    [TestMethod]
    public void ComposeWithoutQueryIsEmptyDraft()
    {
        Assert.AreEqual(string.Empty, Parse("traysky:compose").Text);
    }

    [TestMethod]
    public void ProfileAcceptsBothUriForms()
    {
        Assert.AreEqual("alice.bsky.social", Parse("traysky://profile/alice.bsky.social").Actor);
        Assert.AreEqual("alice.bsky.social", Parse("traysky:profile/@Alice.bsky.social").Actor);
        Assert.AreEqual("did:plc:abc123", Parse("traysky://profile/did:plc:abc123").Actor);
    }

    [TestMethod]
    public void PostFromHandleAndRkey()
    {
        DeepLink link = Parse("traysky://post/alice.bsky.social/3kabc");
        Assert.AreEqual(DeepLinkKind.Post, link.Kind);
        Assert.AreEqual("at://alice.bsky.social/app.bsky.feed.post/3kabc", link.AtUri);
    }

    [TestMethod]
    public void PostFromAtUri()
    {
        DeepLink link = Parse("traysky:post?uri=at%3A%2F%2Fdid%3Aplc%3Aabc%2Fapp.bsky.feed.post%2F3kabc");
        Assert.AreEqual("at://did:plc:abc/app.bsky.feed.post/3kabc", link.AtUri);
    }

    [TestMethod]
    public void HashtagDropsLeadingHash()
    {
        Assert.AreEqual("bluesky", Parse("traysky://hashtag/%23bluesky").Text);
    }

    [TestMethod]
    public void ParsesJoinGroupLinks()
    {
        DeepLink link = Parse("traysky://join/AbC123");
        Assert.AreEqual(DeepLinkKind.JoinGroup, link.Kind);
        Assert.AreEqual("AbC123", link.Text);

        Assert.AreEqual("AbC123", Parse("traysky:join/AbC123").Text);
    }

    [TestMethod]
    public void JoinLinksNeedAUsableCode()
    {
        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri("traysky://join"), out _));
        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri("traysky://join/bad%21code"), out _));
    }

    [TestMethod]
    public void RejectsOtherSchemesAndIncompleteLinks()
    {
        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri("https://bsky.app/profile/alice"), out _));
        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri("traysky://profile"), out _));
        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri("traysky://post/alice.bsky.social"), out _));
        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri("traysky://unknown/x"), out _));
        Assert.IsFalse(DeepLinkPolicy.TryParse(null, out _));
    }

    [TestMethod]
    public void OAuthCallbackIsNotADeepLink()
    {
        Assert.IsFalse(DeepLinkPolicy.TryParse(new Uri(OAuthCallbackPolicy.RedirectUri + "?code=x"), out _));
    }
}

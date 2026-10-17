using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class HashtagFacetPolicyTests
{
    [TestMethod]
    public void HyphenatedTagIsOneTag()
    {
        var spans = HashtagFacetPolicy.Find("Happy #tray-tober all");

        HashtagSpan span = Assert.ContainsSingle(spans);
        Assert.AreEqual("tray-tober", span.Tag);
        Assert.AreEqual(6, span.ByteStart);
        Assert.AreEqual(17, span.ByteEnd);
    }

    [TestMethod]
    public void TrailingPunctuationIsNotPartOfTheTag()
    {
        Assert.AreEqual("tray-tober", Assert.ContainsSingle(HashtagFacetPolicy.Find("it's #tray-tober!")).Tag);
        Assert.AreEqual("tray-tober", Assert.ContainsSingle(HashtagFacetPolicy.Find("#tray-tober-")).Tag);
    }

    [TestMethod]
    public void ByteOffsetsCountUtf8()
    {
        HashtagSpan span = Assert.ContainsSingle(HashtagFacetPolicy.Find("é #tag"));

        Assert.AreEqual(3, span.ByteStart);
        Assert.AreEqual(7, span.ByteEnd);
    }

    [TestMethod]
    [DataRow("#123")]
    [DataRow("a#tag")]
    [DataRow("# ")]
    [DataRow("")]
    public void NonTagsAreIgnored(string text) => Assert.IsEmpty(HashtagFacetPolicy.Find(text));

    [TestMethod]
    [DataRow("#", 0, "")]
    [DataRow("hi #tray-t", 3, "tray-t")]
    [DataRow("a\n#x-y-", 2, "x-y-")]
    [DataRow("tok​#a-b", 4, "a-b")]
    public void OpenTagKeepsHyphens(string before, int start, string query)
    {
        Assert.IsTrue(HashtagFacetPolicy.TryFindOpenTag(before, out int s, out string q));
        Assert.AreEqual(start, s);
        Assert.AreEqual(query, q);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("hi ")]
    [DataRow("hi #tag ")]
    [DataRow("a#tag")]
    [DataRow("#a#b")]
    public void NoOpenTag(string before) => Assert.IsFalse(HashtagFacetPolicy.TryFindOpenTag(before, out _, out _));

    [TestMethod]
    public void FindsEachTag()
    {
        CollectionAssert.AreEqual(new[] { "a", "b-c" }, HashtagFacetPolicy.Find("#a #b-c").Select(s => s.Tag).ToList());
    }
}

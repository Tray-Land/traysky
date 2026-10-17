using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public sealed class SearchQueryPolicyTests
{
    [TestMethod]
    public void Normalize_TrimsAndCapsLength()
    {
        Assert.AreEqual("hello world", SearchQueryPolicy.Normalize("  hello world \n"));
        Assert.AreEqual(string.Empty, SearchQueryPolicy.Normalize(null));
        Assert.AreEqual(SearchQueryPolicy.MaxQueryLength, SearchQueryPolicy.Normalize(new string('a', 1000)).Length);
    }

    [TestMethod]
    [DataRow("#dotnet", "dotnet")]
    [DataRow("  #WinUI ", "WinUI")]
    [DataRow("##double", "double")]
    public void AsHashtag_SingleTag_ReturnsTag(string query, string expected) =>
        Assert.AreEqual(expected, SearchQueryPolicy.AsHashtag(query));

    [TestMethod]
    [DataRow("dotnet")]
    [DataRow("#")]
    [DataRow("##")]
    [DataRow("#two words")]
    [DataRow("#a#b")]
    [DataRow("")]
    public void AsHashtag_NotASingleTag_ReturnsNull(string query) =>
        Assert.IsNull(SearchQueryPolicy.AsHashtag(query));

    [TestMethod]
    public void ShouldSuggest_NeedsTwoCharsAndNotATag()
    {
        Assert.IsFalse(SearchQueryPolicy.ShouldSuggest("a"));
        Assert.IsFalse(SearchQueryPolicy.ShouldSuggest(" a "));
        Assert.IsFalse(SearchQueryPolicy.ShouldSuggest("#dot"));
        Assert.IsTrue(SearchQueryPolicy.ShouldSuggest("jo"));
    }

    [TestMethod]
    public void WithRecentQuery_MovesToFrontAndDedupesIgnoringCase()
    {
        var result = SearchQueryPolicy.WithRecentQuery(["cats", "Dogs", "birds"], "  dogs ");
        CollectionAssert.AreEqual(new[] { "dogs", "cats", "birds" }, result.ToArray());
    }

    [TestMethod]
    public void WithRecentQuery_BlankIsIgnored()
    {
        IReadOnlyList<string> recents = ["cats"];
        Assert.AreSame(recents, SearchQueryPolicy.WithRecentQuery(recents, "   "));
    }

    [TestMethod]
    public void WithRecentQuery_CapsList()
    {
        IReadOnlyList<string> recents = Enumerable.Range(0, SearchQueryPolicy.MaxRecents).Select(i => "q" + i).ToList();
        var result = SearchQueryPolicy.WithRecentQuery(recents, "fresh");
        Assert.AreEqual(SearchQueryPolicy.MaxRecents, result.Count);
        Assert.AreEqual("fresh", result[0]);
        Assert.AreEqual("q" + (SearchQueryPolicy.MaxRecents - 2), result[^1]);
    }

    [TestMethod]
    public void FilterRecents_PrefixMatchExcludesExactTyped()
    {
        IReadOnlyList<string> recents = ["dotnet core", "dotnet", "rust"];
        CollectionAssert.AreEqual(new[] { "dotnet core" }, SearchQueryPolicy.FilterRecents(recents, "DOTNET").ToArray());
        CollectionAssert.AreEqual(recents.ToArray(), SearchQueryPolicy.FilterRecents(recents, "  ").ToArray());
    }

    [TestMethod]
    public void WithoutRecentQuery_RemovesIgnoringCase()
    {
        var result = SearchQueryPolicy.WithoutRecentQuery(["cats", "Dogs"], "dogs");
        CollectionAssert.AreEqual(new[] { "cats" }, result.ToArray());
    }
}

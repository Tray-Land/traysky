using Traysky.Services;

namespace Traysky.Tests;

public class HashtagFacetPolicyTests
{
    [Fact]
    public void HyphenatedTagIsOneTag()
    {
        var spans = HashtagFacetPolicy.Find("Happy #tray-tober all");

        HashtagSpan span = Assert.Single(spans);
        Assert.Equal("tray-tober", span.Tag);
        Assert.Equal(6, span.ByteStart);
        Assert.Equal(17, span.ByteEnd);
    }

    [Fact]
    public void TrailingPunctuationIsNotPartOfTheTag()
    {
        Assert.Equal("tray-tober", Assert.Single(HashtagFacetPolicy.Find("it's #tray-tober!")).Tag);
        Assert.Equal("tray-tober", Assert.Single(HashtagFacetPolicy.Find("#tray-tober-")).Tag);
    }

    [Fact]
    public void ByteOffsetsCountUtf8()
    {
        HashtagSpan span = Assert.Single(HashtagFacetPolicy.Find("é #tag"));

        Assert.Equal(3, span.ByteStart);
        Assert.Equal(7, span.ByteEnd);
    }

    [Theory]
    [InlineData("#123")]
    [InlineData("a#tag")]
    [InlineData("# ")]
    [InlineData("")]
    public void NonTagsAreIgnored(string text) => Assert.Empty(HashtagFacetPolicy.Find(text));

    [Theory]
    [InlineData("#", 0, "")]
    [InlineData("hi #tray-t", 3, "tray-t")]
    [InlineData("a\n#x-y-", 2, "x-y-")]
    [InlineData("tok​#a-b", 4, "a-b")]
    public void OpenTagKeepsHyphens(string before, int start, string query)
    {
        Assert.True(HashtagFacetPolicy.TryFindOpenTag(before, out int s, out string q));
        Assert.Equal(start, s);
        Assert.Equal(query, q);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hi ")]
    [InlineData("hi #tag ")]
    [InlineData("a#tag")]
    [InlineData("#a#b")]
    public void NoOpenTag(string before) => Assert.False(HashtagFacetPolicy.TryFindOpenTag(before, out _, out _));

    [Fact]
    public void FindsEachTag()
    {
        Assert.Equal(["a", "b-c"], HashtagFacetPolicy.Find("#a #b-c").Select(s => s.Tag));
    }
}

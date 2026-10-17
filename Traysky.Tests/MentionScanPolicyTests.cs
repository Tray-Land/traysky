using Traysky.Services;

namespace Traysky.Tests;

public class MentionScanPolicyTests
{
    [Fact]
    public void FindsDistinctHandlesInOrder() =>
        Assert.Equal(["a.bsky.social", "b-c.example.com"], MentionScanPolicy.Handles("hi @a.bsky.social and (@b-c.example.com) @A.bsky.social"));

    [Theory]
    [InlineData("")]
    [InlineData("mail me@foo.com")]
    [InlineData("@nodots")]
    public void NonMentionsAreIgnored(string text) => Assert.Empty(MentionScanPolicy.Handles(text));

    [Fact]
    public void TrailingPunctuationIsNotPartOfTheHandle() =>
        Assert.Equal(["a.bsky.social"], MentionScanPolicy.Handles("thanks @a.bsky.social."));
}

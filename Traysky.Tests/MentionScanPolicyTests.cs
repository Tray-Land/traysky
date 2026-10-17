using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class MentionScanPolicyTests
{
    [TestMethod]
    public void FindsDistinctHandlesInOrder() =>
        CollectionAssert.AreEqual(new[] { "a.bsky.social", "b-c.example.com" }, MentionScanPolicy.Handles("hi @a.bsky.social and (@b-c.example.com) @A.bsky.social").ToList());

    [TestMethod]
    [DataRow("")]
    [DataRow("mail me@foo.com")]
    [DataRow("@nodots")]
    public void NonMentionsAreIgnored(string text) => Assert.IsEmpty(MentionScanPolicy.Handles(text));

    [TestMethod]
    public void TrailingPunctuationIsNotPartOfTheHandle() =>
        CollectionAssert.AreEqual(new[] { "a.bsky.social" }, MentionScanPolicy.Handles("thanks @a.bsky.social.").ToList());
}

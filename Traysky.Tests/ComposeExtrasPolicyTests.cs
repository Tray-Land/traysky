using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ComposeExtrasPolicyTests
{
    [TestMethod]
    public void Resolve_PrefersSavedChoice()
    {
        CollectionAssert.AreEqual(new[] { "es", "fr" }, ComposeLanguagePolicy.Resolve("es,fr", "en").ToArray());
    }

    [TestMethod]
    public void Resolve_FallsBackToUiLanguageThenEnglish()
    {
        CollectionAssert.AreEqual(new[] { "de" }, ComposeLanguagePolicy.Resolve("", "DE").ToArray());
        CollectionAssert.AreEqual(new[] { "en" }, ComposeLanguagePolicy.Resolve(null, "xx").ToArray());
    }

    [TestMethod]
    public void Parse_DropsUnknownRepeatedAndExtraCodes()
    {
        CollectionAssert.AreEqual(new[] { "en", "ja", "ko" }, ComposeLanguagePolicy.Parse("en, EN, zz, ja, ko, fr").ToArray());
    }

    [TestMethod]
    public void Toggle_AddsRemovesAndCapsAtThree()
    {
        var chosen = ComposeLanguagePolicy.Toggle(["en"], "fr");
        CollectionAssert.AreEqual(new[] { "en", "fr" }, chosen.ToArray());

        chosen = ComposeLanguagePolicy.Toggle(chosen, "de");
        Assert.AreSame(chosen, ComposeLanguagePolicy.Toggle(chosen, "es"), "a fourth language is refused");

        chosen = ComposeLanguagePolicy.Toggle(chosen, "fr");
        CollectionAssert.AreEqual(new[] { "en", "de" }, chosen.ToArray());
    }

    [TestMethod]
    public void Toggle_KeepsTheLastLanguage()
    {
        IReadOnlyList<string> only = ["en"];
        Assert.AreSame(only, ComposeLanguagePolicy.Toggle(only, "en"));
    }

    [TestMethod]
    public void Labels_ReadWell()
    {
        Assert.AreEqual("EN, ES", ComposeLanguagePolicy.Label(["en", "es"]));
        Assert.AreEqual("English, Spanish", ComposeLanguagePolicy.Description(["en", "es"]));
    }

    [TestMethod]
    public void ContentLabel_ValuesMatchBlueskyLabels()
    {
        Assert.IsNull(ContentLabelPolicy.Value(ComposeContentLabel.None));
        Assert.AreEqual("sexual", ContentLabelPolicy.Value(ComposeContentLabel.Suggestive));
        Assert.AreEqual("nudity", ContentLabelPolicy.Value(ComposeContentLabel.Nudity));
        Assert.AreEqual("porn", ContentLabelPolicy.Value(ComposeContentLabel.Adult));
        Assert.AreEqual("graphic-media", ContentLabelPolicy.Value(ComposeContentLabel.Graphic));
    }

    [TestMethod]
    public void ContentLabel_NeedsMedia()
    {
        Assert.AreEqual(ComposeContentLabel.None, ContentLabelPolicy.Effective(ComposeContentLabel.Adult, hasMedia: false));
        Assert.AreEqual(ComposeContentLabel.Adult, ContentLabelPolicy.Effective(ComposeContentLabel.Adult, hasMedia: true));
        Assert.IsFalse(ContentLabelPolicy.CanLabel(false));
    }

    [TestMethod]
    public void Thread_CapsAtTwentyFivePostsIncludingTheFirst()
    {
        Assert.IsTrue(ThreadPostPolicy.CanAdd(24));
        Assert.IsFalse(ThreadPostPolicy.CanAdd(ThreadPostPolicy.MaxPosts));
    }

    [TestMethod]
    public void Thread_SendableTextsTrimAndSkipBlanks()
    {
        CollectionAssert.AreEqual(new[] { "two", "four" }, ThreadPostPolicy.SendableTexts(["  two ", "", null, "   ", "four"]).ToArray());
    }

    [TestMethod]
    public void Thread_PostWithOnlyMediaIsSendable()
    {
        Assert.IsTrue(ThreadPostPolicy.IsSendable("  ", 1));
        Assert.IsTrue(ThreadPostPolicy.IsSendable("hi", 0));
        Assert.IsFalse(ThreadPostPolicy.IsSendable(" ", 0));
        Assert.IsFalse(ThreadPostPolicy.IsSendable(null, 0));
    }

    [TestMethod]
    public void Thread_AllFitRejectsAnOverlongPost()
    {
        Assert.IsTrue(ThreadPostPolicy.AllFit(["short", null]));
        Assert.IsFalse(ThreadPostPolicy.AllFit(["short", new string('a', PostTextPolicy.MaxGraphemes + 1)]));
    }

    [TestMethod]
    public void Thread_PartialFailureNoteCountsFirstPost()
    {
        StringAssert.StartsWith(ThreadPostPolicy.PartialFailureNote(2, 4), "Posted 2 of 4.");
    }
}

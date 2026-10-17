using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class DraftPolicyTests
{
    [TestMethod]
    public void CanSave_NeedsTextAQuoteOrMedia()
    {
        Assert.IsFalse(DraftPolicy.CanSave("  \n", hasQuote: false, attachmentCount: 0));
        Assert.IsFalse(DraftPolicy.CanSave(null, hasQuote: false, attachmentCount: 0));
        Assert.IsTrue(DraftPolicy.CanSave("hi", hasQuote: false, attachmentCount: 0));
        Assert.IsTrue(DraftPolicy.CanSave("", hasQuote: true, attachmentCount: 0));
        Assert.IsTrue(DraftPolicy.CanSave("", hasQuote: false, attachmentCount: 1));
    }

    [TestMethod]
    public void HasStorableContent_MediaAloneIsNotEnough()
    {
        Assert.IsFalse(DraftPolicy.HasStorableContent("", hasQuote: false));
        Assert.IsTrue(DraftPolicy.HasStorableContent("hi", hasQuote: false));
        Assert.IsTrue(DraftPolicy.HasStorableContent("", hasQuote: true));
    }

    [TestMethod]
    public void Preview_FlattensWhitespaceAndCutsLongText()
    {
        Assert.AreEqual("first line second line", DraftPolicy.Preview("  first line\n\n  second   line "));
        Assert.AreEqual("(empty draft)", DraftPolicy.Preview("   "));
        Assert.AreEqual("(quote with no comment)", DraftPolicy.Preview("", hasQuote: true));

        string preview = DraftPolicy.Preview(new string('a', 200));
        Assert.AreEqual(DraftPolicy.PreviewLength + 1, preview.Length);
        Assert.IsTrue(preview.EndsWith('…'));
    }

    [TestMethod]
    public void SaveNote_NamesWhatIsLeftOut()
    {
        Assert.IsNull(DraftPolicy.SaveNote(0, hasVideo: false));
        StringAssert.Contains(DraftPolicy.SaveNote(0, hasVideo: true), "video");
        StringAssert.Contains(DraftPolicy.SaveNote(1, hasVideo: false), "image isn't");
        StringAssert.Contains(DraftPolicy.SaveNote(3, hasVideo: false), "3 images");
    }

    [TestMethod]
    public void SaveNote_MentionsAReplyTargetThatIsNotKept()
    {
        StringAssert.Contains(DraftPolicy.SaveNote(0, hasVideo: false, isReply: true), "replying to");

        string both = DraftPolicy.SaveNote(2, hasVideo: false, isReply: true)!;
        StringAssert.Contains(both, "2 images");
        StringAssert.Contains(both, "replying to");
    }

    [TestMethod]
    public void RestoreNote_OnlyWhenThereWasMedia()
    {
        Assert.IsNull(DraftPolicy.RestoreNote(0, 0));
        StringAssert.Contains(DraftPolicy.RestoreNote(2, 0), "2 images");
        StringAssert.Contains(DraftPolicy.RestoreNote(0, 1), "1 video");
        StringAssert.Contains(DraftPolicy.RestoreNote(1, 1), "2 media");
    }

    [TestMethod]
    public void IsFromThisDevice_MatchesTheStoredId()
    {
        string id = DraftPolicy.DeviceId(" JOE-PC ");

        Assert.AreEqual("traysky:joe-pc", id);
        Assert.IsTrue(DraftPolicy.IsFromThisDevice(id, id));
        Assert.IsFalse(DraftPolicy.IsFromThisDevice("other", id));
        Assert.IsFalse(DraftPolicy.IsFromThisDevice(null, id));
    }
}

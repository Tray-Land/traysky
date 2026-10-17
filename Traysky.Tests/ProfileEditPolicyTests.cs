using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ProfileEditPolicyTests
{
    [TestMethod]
    public void DisplayNameError_AllowsEmptyAndTheLimit()
    {
        Assert.IsNull(ProfileEditPolicy.DisplayNameError(null));
        Assert.IsNull(ProfileEditPolicy.DisplayNameError("   "));
        Assert.IsNull(ProfileEditPolicy.DisplayNameError(new string('a', ProfileEditPolicy.DisplayNameMaxGraphemes)));
    }

    [TestMethod]
    public void DisplayNameError_RejectsOverTheGraphemeLimit()
    {
        Assert.IsNotNull(ProfileEditPolicy.DisplayNameError(new string('a', ProfileEditPolicy.DisplayNameMaxGraphemes + 1)));
    }

    [TestMethod]
    public void DisplayNameError_IgnoresLeadingAndTrailingSpace()
    {
        string padded = "  " + new string('a', ProfileEditPolicy.DisplayNameMaxGraphemes) + "  ";

        Assert.IsNull(ProfileEditPolicy.DisplayNameError(padded));
    }

    [TestMethod]
    public void GraphemeCount_CountsAFamilyEmojiAsOne()
    {
        string family = "\U0001F468‍\U0001F469‍\U0001F467";

        Assert.AreEqual(1, ProfileEditPolicy.GraphemeCount(family));
        Assert.AreEqual(0, ProfileEditPolicy.GraphemeCount(null));
        Assert.AreEqual(3, ProfileEditPolicy.GraphemeCount("abc"));
    }

    [TestMethod]
    public void BioError_RejectsOverTheGraphemeLimit()
    {
        Assert.IsNull(ProfileEditPolicy.BioError(new string('a', ProfileEditPolicy.BioMaxGraphemes)));
        Assert.IsNotNull(ProfileEditPolicy.BioError(new string('a', ProfileEditPolicy.BioMaxGraphemes + 1)));
    }

    [TestMethod]
    public void BioError_RejectsOverTheByteLimitEvenWhenGraphemesFit()
    {
        // 256 four-byte characters is 1024 bytes: fine. 256 graphemes of ten code points each can't be built
        // under the grapheme cap without exceeding 2560 bytes, so check the byte rule with a combining run.
        string heavy = "a" + new string('́', 3000); // one grapheme, 6001 bytes

        Assert.AreEqual(1, ProfileEditPolicy.GraphemeCount(heavy));
        Assert.IsNotNull(ProfileEditPolicy.BioError(heavy));
    }

    [TestMethod]
    public void Validate_ReportsTheNameBeforeTheBio()
    {
        string tooLongName = new string('a', ProfileEditPolicy.DisplayNameMaxGraphemes + 1);
        string tooLongBio = new string('a', ProfileEditPolicy.BioMaxGraphemes + 1);

        StringAssert.Contains(ProfileEditPolicy.Validate(tooLongName, tooLongBio)!, "Display name");
        StringAssert.Contains(ProfileEditPolicy.Validate("ok", tooLongBio)!, "Bio");
        Assert.IsNull(ProfileEditPolicy.Validate("ok", "fine"));
    }

    [TestMethod]
    public void Normalize_TrimsAndTurnsEmptyIntoNull()
    {
        Assert.AreEqual("Joe", ProfileEditPolicy.Normalize("  Joe "));
        Assert.IsNull(ProfileEditPolicy.Normalize("   "));
        Assert.IsNull(ProfileEditPolicy.Normalize(null));
    }

    [TestMethod]
    public void Counter_ShowsGraphemesOverTheLimit()
    {
        Assert.AreEqual("3 / 64", ProfileEditPolicy.Counter("abc", 64));
    }

    [TestMethod]
    public void HasChanges_IgnoresWhitespaceAndNullVersusEmpty()
    {
        Assert.IsFalse(ProfileEditPolicy.HasChanges("Joe", null, " Joe ", "", ProfileImageChange.Keep, ProfileImageChange.Keep));
        Assert.IsTrue(ProfileEditPolicy.HasChanges("Joe", null, "Joseph", null, ProfileImageChange.Keep, ProfileImageChange.Keep));
        Assert.IsTrue(ProfileEditPolicy.HasChanges("Joe", "hi", "Joe", "hello", ProfileImageChange.Keep, ProfileImageChange.Keep));
    }

    [TestMethod]
    public void HasChanges_AnImageChangeCounts()
    {
        Assert.IsTrue(ProfileEditPolicy.HasChanges("Joe", null, "Joe", null, ProfileImageChange.Replace, ProfileImageChange.Keep));
        Assert.IsTrue(ProfileEditPolicy.HasChanges("Joe", null, "Joe", null, ProfileImageChange.Keep, ProfileImageChange.Remove));
    }

    [TestMethod]
    public void ImageSteps_NeverUpscaleAndEndSmallerThanTheyStart()
    {
        foreach (var steps in new[] { ProfileEditPolicy.AvatarSteps, ProfileEditPolicy.BannerSteps })
        {
            Assert.IsTrue(steps.Count > 1);
            Assert.IsTrue(steps[^1].MaxLongEdge < steps[0].MaxLongEdge);
            Assert.IsTrue(steps[^1].Quality < steps[0].Quality);
        }
    }
}

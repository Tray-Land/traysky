using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ComposeDropPolicyTests
{
    [TestMethod]
    public void Files_AreMedia()
    {
        Assert.AreEqual(ComposeDropKind.Media, ComposeDropPolicy.Classify(true, false, false, false));
    }

    [TestMethod]
    public void Bitmap_IsMedia()
    {
        Assert.AreEqual(ComposeDropKind.Media, ComposeDropPolicy.Classify(false, true, false, false));
    }

    [TestMethod]
    public void BrowserImage_WithTextAndLink_IsMedia()
    {
        Assert.AreEqual(ComposeDropKind.Media, ComposeDropPolicy.Classify(false, true, true, true));
    }

    [TestMethod]
    public void TextOrLink_IsText()
    {
        Assert.AreEqual(ComposeDropKind.Text, ComposeDropPolicy.Classify(false, false, true, false));
        Assert.AreEqual(ComposeDropKind.Text, ComposeDropPolicy.Classify(false, false, false, true));
    }

    [TestMethod]
    public void Nothing_IsNone()
    {
        Assert.AreEqual(ComposeDropKind.None, ComposeDropPolicy.Classify(false, false, false, false));
        Assert.AreEqual(string.Empty, ComposeDropPolicy.Caption(ComposeDropKind.None));
    }

    [TestMethod]
    public void Captions_DifferByKind()
    {
        Assert.AreNotEqual(ComposeDropPolicy.Caption(ComposeDropKind.Media), ComposeDropPolicy.Caption(ComposeDropKind.Text));
    }
}

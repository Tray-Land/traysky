using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class VideoUploadPolicyTests
{
    [TestMethod]
    public void Split_ExactMultiple()
    {
        var parts = VideoUploadPolicy.Split(30, 10, 3)!;
        Assert.AreEqual(3, parts.Count);
        Assert.IsTrue(parts.All(p => p.Length == 10));
        Assert.AreEqual(20, parts[2].Offset);
    }

    [TestMethod]
    public void Split_ShortLastPart()
    {
        var parts = VideoUploadPolicy.Split(25, 10, 3)!;
        Assert.AreEqual(5, parts[2].Length);
        Assert.AreEqual(25, parts.Sum(p => p.Length));
    }

    [TestMethod]
    public void Split_SinglePart()
    {
        var parts = VideoUploadPolicy.Split(7, 10, 1)!;
        Assert.AreEqual(1, parts.Count);
        Assert.AreEqual(7, parts[0].Length);
    }

    [TestMethod]
    public void Split_NumbersStartAtFirstPartNumber()
    {
        var parts = VideoUploadPolicy.Split(30, 10, 3)!;
        Assert.AreEqual(VideoUploadPolicy.FirstPartNumber, parts[0].Number);
        Assert.AreEqual(VideoUploadPolicy.FirstPartNumber + 2, parts[2].Number);
    }

    [TestMethod]
    public void Split_PartCountMismatch_ReturnsNull()
    {
        Assert.IsNull(VideoUploadPolicy.Split(30, 10, 2));
        Assert.IsNull(VideoUploadPolicy.Split(30, 10, 4));
    }

    [TestMethod]
    public void Split_InvalidSizes_ReturnsNull()
    {
        Assert.IsNull(VideoUploadPolicy.Split(0, 10, 0));
        Assert.IsNull(VideoUploadPolicy.Split(10, 0, 1));
        Assert.IsNull(VideoUploadPolicy.Split(-1, 10, 1));
    }

    [TestMethod]
    public void Fraction_ClampsAndHandlesZero()
    {
        Assert.AreEqual(0.5, VideoUploadPolicy.Fraction(5, 10), 1e-9);
        Assert.AreEqual(1.0, VideoUploadPolicy.Fraction(20, 10), 1e-9);
        Assert.AreEqual(0.0, VideoUploadPolicy.Fraction(5, 0), 1e-9);
        Assert.AreEqual(0.0, VideoUploadPolicy.Fraction(-5, 10), 1e-9);
    }

    [TestMethod]
    public void NeedsSending_SkipsReceived()
    {
        var part = new VideoPart(2, 10, 10);
        Assert.IsTrue(VideoUploadPolicy.NeedsSending(part, null));
        Assert.IsTrue(VideoUploadPolicy.NeedsSending(part, new long[] { 1, 3 }));
        Assert.IsFalse(VideoUploadPolicy.NeedsSending(part, new long[] { 1, 2 }));
    }

    [TestMethod]
    public void ValidateVideoLimits_Allowed()
    {
        Assert.IsNull(ComposeAttachmentPolicy.ValidateVideoLimits(true, 5, 100_000_000, 50_000_000));
        Assert.IsNull(ComposeAttachmentPolicy.ValidateVideoLimits(true, null, null, 50_000_000));
    }

    [TestMethod]
    public void ValidateVideoLimits_CannotUpload()
    {
        Assert.IsNotNull(ComposeAttachmentPolicy.ValidateVideoLimits(false, 5, 100, 1));
    }

    [TestMethod]
    public void ValidateVideoLimits_NoVideosLeft()
    {
        Assert.IsNotNull(ComposeAttachmentPolicy.ValidateVideoLimits(true, 0, 100_000_000, 1));
    }

    [TestMethod]
    public void ValidateVideoLimits_ExactlyEnoughBytes_IsAllowed()
    {
        Assert.IsNull(ComposeAttachmentPolicy.ValidateVideoLimits(true, 1, 1000, 1000));
        Assert.IsNotNull(ComposeAttachmentPolicy.ValidateVideoLimits(true, 1, 1000, 1001));
    }

    [TestMethod]
    public void ValidateVideoLimits_NoBytesLeft_UsesDailyLimitMessage()
    {
        string? message = ComposeAttachmentPolicy.ValidateVideoLimits(true, 3, 0, 10);
        StringAssert.Contains(message, "limit");
    }

    [TestMethod]
    public void LimitsSummary_Variants()
    {
        Assert.IsNull(ComposeAttachmentPolicy.LimitsSummary(null, null));
        Assert.AreEqual("1 video left today", ComposeAttachmentPolicy.LimitsSummary(1, null));
        Assert.AreEqual("3 videos left today", ComposeAttachmentPolicy.LimitsSummary(3, null));
        Assert.AreEqual("120 MB left today", ComposeAttachmentPolicy.LimitsSummary(null, 120_000_000));
        Assert.AreEqual("2 videos, 1.5 MB left today", ComposeAttachmentPolicy.LimitsSummary(2, 1_500_000));
        Assert.AreEqual("0 videos left today", ComposeAttachmentPolicy.LimitsSummary(-1, null));
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class ImageCompressionPolicyTests
{
    [TestMethod]
    public void ScaledSize_LeavesSmallImagesAlone()
    {
        Assert.AreEqual((800, 600), ImageCompressionPolicy.ScaledSize(800, 600, 2048));
        Assert.AreEqual((800, 600), ImageCompressionPolicy.ScaledSize(800, 600, null));
    }

    [TestMethod]
    public void ScaledSize_CapsLongEdgeKeepingAspect()
    {
        Assert.AreEqual((2048, 1024), ImageCompressionPolicy.ScaledSize(4096, 2048, 2048));
        Assert.AreEqual((1024, 2048), ImageCompressionPolicy.ScaledSize(2048, 4096, 2048));
    }

    [TestMethod]
    public void ScaledSize_NeverReturnsZero()
    {
        (int w, int h) = ImageCompressionPolicy.ScaledSize(10000, 1, 100);
        Assert.AreEqual(100, w);
        Assert.AreEqual(1, h);
    }

    [TestMethod]
    public void Gif_IsNotCompressible()
    {
        Assert.IsFalse(ImageCompressionPolicy.CanCompress("image/gif"));
        Assert.IsTrue(ImageCompressionPolicy.CanCompress("image/png"));
    }

    [TestMethod]
    public void Ladders_GetMonotonicallyMoreAggressive()
    {
        foreach (var ladder in new[] { ImageCompressionPolicy.GentleSteps, ImageCompressionPolicy.LossySteps })
            for (int i = 1; i < ladder.Count; i++)
                Assert.IsTrue(ladder[i].Quality <= ladder[i - 1].Quality);

        Assert.IsTrue(ImageCompressionPolicy.GentleSteps.All(s => s.MaxLongEdge is null));
        Assert.IsTrue(ImageCompressionPolicy.LossySteps.All(s => s.MaxLongEdge is not null));
    }
}

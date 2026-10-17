using Microsoft.VisualStudio.TestTools.UnitTesting;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class PopoutPlacementPolicyTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [TestMethod]
    public void PrefersLeftOfPopupWithGap()
    {
        var popup = new PixelRect(1500, 400, 400, 620);

        (int x, int y) = PopoutPlacementPolicy.Place(popup, Work, 380, 560, 12);

        Assert.AreEqual(1500 - 12 - 380, x);
        Assert.IsTrue(x + 380 + 12 <= popup.X);
    }

    [TestMethod]
    public void FallsBackToRightWhenNoRoomOnLeft()
    {
        var popup = new PixelRect(100, 100, 400, 600);

        (int x, _) = PopoutPlacementPolicy.Place(popup, Work, 380, 560, 12);

        Assert.AreEqual(512, x);
    }

    [TestMethod]
    public void BottomAlignsWhenPopupSitsOnTheBottomEdge()
    {
        var popup = new PixelRect(1500, 420, 400, 600);

        (_, int y) = PopoutPlacementPolicy.Place(popup, Work, 380, 560, 12);

        Assert.AreEqual(popup.Bottom - 560, y);
    }

    [TestMethod]
    public void StaysInsideWorkAreaWhenThereIsNoRoom()
    {
        var narrow = new PixelRect(0, 0, 500, 400);
        var popup = new PixelRect(50, 0, 400, 400);

        (int x, int y) = PopoutPlacementPolicy.Place(popup, narrow, 480, 380, 12);

        Assert.IsTrue(x is >= 0 and <= 20);
        Assert.IsTrue(y is >= 0 and <= 20);
    }
}

using Traysky.Services;

namespace Traysky.Tests;

public class PopoutPlacementPolicyTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void PrefersLeftOfPopupWithGap()
    {
        var popup = new PixelRect(1500, 400, 400, 620);

        (int x, int y) = PopoutPlacementPolicy.Place(popup, Work, 380, 560, 12);

        Assert.Equal(1500 - 12 - 380, x);
        Assert.True(x + 380 + 12 <= popup.X);
    }

    [Fact]
    public void FallsBackToRightWhenNoRoomOnLeft()
    {
        var popup = new PixelRect(100, 100, 400, 600);

        (int x, _) = PopoutPlacementPolicy.Place(popup, Work, 380, 560, 12);

        Assert.Equal(512, x);
    }

    [Fact]
    public void BottomAlignsWhenPopupSitsOnTheBottomEdge()
    {
        var popup = new PixelRect(1500, 420, 400, 600);

        (_, int y) = PopoutPlacementPolicy.Place(popup, Work, 380, 560, 12);

        Assert.Equal(popup.Bottom - 560, y);
    }

    [Fact]
    public void StaysInsideWorkAreaWhenThereIsNoRoom()
    {
        var narrow = new PixelRect(0, 0, 500, 400);
        var popup = new PixelRect(50, 0, 400, 400);

        (int x, int y) = PopoutPlacementPolicy.Place(popup, narrow, 480, 380, 12);

        Assert.InRange(x, 0, 20);
        Assert.InRange(y, 0, 20);
    }
}

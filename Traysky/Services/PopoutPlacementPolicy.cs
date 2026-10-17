using System;

namespace Traysky.Services;

/// <summary>A rectangle in physical pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>Where a companion window goes next to the tray popup: beside it, never over it.</summary>
public static class PopoutPlacementPolicy
{
    /// <summary>
    /// Left of <paramref name="anchor"/> when there's room (the popup lives at the right edge by
    /// the tray), else right of it, always <paramref name="gap"/> clear and top-aligned with it.
    /// With no room on either side it is pinned inside <paramref name="workArea"/> instead,
    /// overlapping, because a window that can't be reached is worse.
    /// </summary>
    public static (int X, int Y) Place(PixelRect anchor, PixelRect workArea, int width, int height, int gap)
    {
        int x;
        if (anchor.X - gap - width >= workArea.X)
            x = anchor.X - gap - width;
        else if (anchor.Right + gap + width <= workArea.Right)
            x = anchor.Right + gap;
        else
            x = Math.Max(workArea.X, workArea.Right - width);

        // A popup at the bottom edge is usually taller than the companion, so line up bottoms
        // then; otherwise tops. Either way the window stays on screen.
        int y = anchor.Bottom >= workArea.Bottom - gap * 2 ? anchor.Bottom - height : anchor.Y;
        y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));

        return (x, y);
    }
}

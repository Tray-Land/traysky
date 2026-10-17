using System;
using System.Collections.Generic;

namespace Traysky.Services;

/// <summary>
/// How oversized images get shrunk to fit Bluesky's blob limit. Pure .NET; the WinRT encoding
/// lives in <c>ImageCompressor</c>. Two ladders: <see cref="GentleSteps"/> keeps every pixel and
/// only raises JPEG compression (applied automatically), <see cref="LossySteps"/> also downsizes
/// (applied when the user presses "Compress").
/// </summary>
public static class ImageCompressionPolicy
{
    /// <summary>One attempt: optionally cap the long edge, then encode as JPEG at this quality (0..1).</summary>
    public readonly record struct Step(int? MaxLongEdge, double Quality);

    public static IReadOnlyList<Step> GentleSteps { get; } =
    [
        new(null, 0.92),
        new(null, 0.85),
        new(null, 0.80),
    ];

    public static IReadOnlyList<Step> LossySteps { get; } =
    [
        new(2048, 0.80),
        new(2048, 0.65),
        new(1600, 0.60),
        new(1280, 0.50),
        new(1024, 0.40),
    ];

    /// <summary>Animated GIFs would lose their animation when re-encoded, so they are never compressed.</summary>
    public static bool CanCompress(string mimeType) =>
        !string.Equals(mimeType, "image/gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>The size after capping the long edge at <paramref name="maxLongEdge"/>; never upscales, never returns 0.</summary>
    public static (int Width, int Height) ScaledSize(int width, int height, int? maxLongEdge)
    {
        int longEdge = Math.Max(width, height);
        if (maxLongEdge is not { } cap || cap <= 0 || longEdge <= cap)
            return (width, height);

        double ratio = (double)cap / longEdge;
        return (Math.Max(1, (int)Math.Round(width * ratio)), Math.Max(1, (int)Math.Round(height * ratio)));
    }
}

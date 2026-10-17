using System;
using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>One slice of a video file for a multipart upload.</summary>
public readonly record struct VideoPart(long Number, int Offset, int Length);

/// <summary>
/// Pure helpers for the video service's multipart upload: carving the file into the parts the
/// server asked for and turning bytes sent into a progress fraction. The service dictates the part
/// size and count (<c>startUpload</c>); each part's length must match exactly what it derives from
/// the declared size, so the split has to mirror that arithmetic.
/// </summary>
public static class VideoUploadPolicy
{
    /// <summary>
    /// The SDK doesn't document whether part numbers start at 0 or 1 (S3-style multipart is 1-based).
    /// Kept in one place so correcting it after a real upload is a one-line change.
    /// </summary>
    public const long FirstPartNumber = 1;

    /// <summary>
    /// Splits <paramref name="totalBytes"/> into parts of <paramref name="partSize"/> (the last may be shorter).
    /// Returns null if the server's part count doesn't match what the sizes imply, so a confused response
    /// falls back to the single-request upload instead of sending a malformed multipart upload.
    /// </summary>
    public static IReadOnlyList<VideoPart>? Split(long totalBytes, long partSize, long partCount)
    {
        if (totalBytes <= 0 || partSize <= 0 || totalBytes > int.MaxValue || partSize > int.MaxValue)
            return null;

        long expected = (totalBytes + partSize - 1) / partSize;
        if (expected != partCount)
            return null;

        var parts = new List<VideoPart>((int)expected);
        for (long i = 0; i < expected; i++)
        {
            long offset = i * partSize;
            parts.Add(new VideoPart(FirstPartNumber + i, (int)offset, (int)Math.Min(partSize, totalBytes - offset)));
        }

        return parts;
    }

    /// <summary>Fraction (0–1) of the upload sent. Clamped so a stray extra part can't overshoot.</summary>
    public static double Fraction(long sentBytes, long totalBytes) =>
        totalBytes <= 0 ? 0 : Math.Clamp((double)sentBytes / totalBytes, 0, 1);

    /// <summary>
    /// Whether a part still has to be sent when resuming: false if the server already reports it received.
    /// </summary>
    public static bool NeedsSending(VideoPart part, IReadOnlyCollection<long>? receivedParts) =>
        receivedParts is null || !receivedParts.Contains(part.Number);
}

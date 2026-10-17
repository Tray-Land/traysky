using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Traysky.Services;

/// <summary>The result of a successful <see cref="ImageCompressor.TryCompressAsync"/>; dimensions are as displayed (EXIF-rotated).</summary>
public sealed record CompressedImage(byte[] Bytes, int Width, int Height);

/// <summary>
/// Re-encodes an image as JPEG, walking an <see cref="ImageCompressionPolicy"/> ladder until the
/// result fits. WinRT-only; the decisions about which steps to take live in the policy.
/// Transparent pixels come out black, as JPEG has no alpha.
/// </summary>
public static class ImageCompressor
{
    public static async Task<CompressedImage?> TryCompressAsync(byte[] source, IReadOnlyList<ImageCompressionPolicy.Step> steps, long maxBytes)
    {
        using InMemoryRandomAccessStream input = new();
        using (DataWriter writer = new(input))
        {
            writer.WriteBytes(source);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        input.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input);
        int rawWidth = (int)decoder.PixelWidth;
        int rawHeight = (int)decoder.PixelHeight;

        foreach (ImageCompressionPolicy.Step step in steps)
        {
            (int width, int height) = ImageCompressionPolicy.ScaledSize(rawWidth, rawHeight, step.MaxLongEdge);
            BitmapTransform transform = new()
            {
                ScaledWidth = (uint)width,
                ScaledHeight = (uint)height,
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            BitmapPropertySet quality = new()
            {
                ["ImageQuality"] = new BitmapTypedValue((float)step.Quality, PropertyType.Single)
            };

            using InMemoryRandomAccessStream output = new();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, quality);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            if (output.Size == 0 || output.Size > (ulong)maxBytes)
            {
                bitmap.Dispose();
                continue;
            }

            byte[] bytes = new byte[output.Size];
            output.Seek(0);
            using (DataReader reader = new(output))
            {
                await reader.LoadAsync((uint)output.Size);
                reader.ReadBytes(bytes);
                reader.DetachStream();
            }

            // The bitmap is already rotated, so its size is the displayed size.
            int outWidth = bitmap.PixelWidth, outHeight = bitmap.PixelHeight;
            bitmap.Dispose();
            return new CompressedImage(bytes, outWidth, outHeight);
        }

        return null;
    }
}

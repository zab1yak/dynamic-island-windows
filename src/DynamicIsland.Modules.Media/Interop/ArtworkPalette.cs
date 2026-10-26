using System.Runtime.InteropServices;
using DynamicIsland.Modules.Media.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DynamicIsland.Modules.Media.Interop;

/// <summary>Decodes artwork with WinRT (no WPF) and computes its muted accent colour.</summary>
internal static class ArtworkPalette
{
    private const uint SampleSize = 48;

    /// <summary>Returns the accent of <paramref name="image"/>, or the neutral fallback.</summary>
    public static async Task<Rgb> AccentAsync(byte[]? image)
    {
        if (image is null || image.Length == 0)
        {
            return DominantColor.Fallback;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(image);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 4096 || decoder.PixelHeight > 4096)
            {
                return DominantColor.Fallback;
            }

            var transform = new BitmapTransform { ScaledWidth = SampleSize, ScaledHeight = SampleSize, InterpolationMode = BitmapInterpolationMode.Linear };
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return DominantColor.FromBgra(pixels.DetachPixelData());
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return DominantColor.Fallback;
        }
    }
}

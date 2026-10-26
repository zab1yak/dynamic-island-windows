using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.UI.Converters;

/// <summary>Encoded image bytes → frozen <see cref="BitmapImage"/> (decoded small, cached per array).</summary>
public sealed class BytesToImageConverter : IValueConverter
{
    private static readonly ConditionalWeakTable<byte[], BitmapImage> Cache = new();

    private const int MaxDimension = 4096;
    private const long MaxPixels = 16_000_000;

    /// <summary>Decode width in pixels (artwork is shown at most ~84 DIP, 2x for high DPI).</summary>
    public int DecodePixelWidth { get; set; } = 192;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        if (Cache.TryGetValue(bytes, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            // Untrusted artwork: read the header first and refuse decompression bombs.
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || frame.PixelWidth > MaxDimension || frame.PixelHeight > MaxDimension
                || (long)frame.PixelWidth * frame.PixelHeight > MaxPixels)
            {
                return null;
            }

            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = DecodePixelWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Cache.AddOrUpdate(bytes, image);
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException
            or FormatException or OutOfMemoryException or System.Runtime.InteropServices.COMException or IOException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>ARGB <see cref="uint"/> → frozen brush; <c>ConverterParameter</c> = opacity 0–1.</summary>
public sealed class ArgbToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var argb = value is uint u ? u : 0xFF8E8E93u;
        var opacity = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? o : 1.0;
        var brush = new SolidColorBrush(Color.FromArgb((byte)((argb >> 24) * Math.Clamp(opacity, 0, 1)), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>bool → Visibility; <c>ConverterParameter="Invert"</c> flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is "Invert")
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null or empty → Collapsed; anything else → Visible (<c>Invert</c> flips).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value is not null && (value is not string s || s.Length > 0);
        if (parameter is "Invert")
        {
            present = !present;
        }

        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

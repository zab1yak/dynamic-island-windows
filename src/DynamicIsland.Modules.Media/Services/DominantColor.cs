namespace DynamicIsland.Modules.Media.Services;

/// <summary>An opaque RGB colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>ARGB value with full alpha.</summary>
    public uint ToArgb() => 0xFF000000u | ((uint)R << 16) | ((uint)G << 8) | B;
}

/// <summary>
/// Picks an accent colour from artwork: the average of the most saturated, mid-bright pixels,
/// then toned down so it sits quietly on the black island.
/// </summary>
public static class DominantColor
{
    /// <summary>Neutral fallback (iOS secondary grey).</summary>
    public static Rgb Fallback { get; } = new(0x8E, 0x8E, 0x93);

    /// <summary>Computes the accent from BGRA32 pixels.</summary>
    /// <param name="bgra">Pixel data, 4 bytes per pixel (B, G, R, A).</param>
    public static Rgb FromBgra(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length < 4)
        {
            return Fallback;
        }

        // Sample at most ~4k pixels; artwork is usually a few hundred pixels square.
        var pixels = bgra.Length / 4;
        var step = Math.Max(1, pixels / 4096);
        double r = 0, g = 0, b = 0, weightSum = 0;
        for (var i = 0; i < pixels; i += step)
        {
            var o = i * 4;
            if (bgra[o + 3] < 128)
            {
                continue;
            }

            var (_, s, l) = ToHsl(bgra[o + 2], bgra[o + 1], bgra[o]);
            // Prefer colourful, mid-light pixels; ignore near-black and near-white backgrounds.
            if (l < 0.12 || l > 0.9)
            {
                continue;
            }

            var weight = s * s * (1 - Math.Abs(l - 0.5));
            r += bgra[o + 2] * weight;
            g += bgra[o + 1] * weight;
            b += bgra[o] * weight;
            weightSum += weight;
        }

        if (weightSum < 1e-6)
        {
            return Fallback;
        }

        return Mute(new Rgb((byte)(r / weightSum), (byte)(g / weightSum), (byte)(b / weightSum)));
    }

    /// <summary>Caps saturation and lightness so the accent never shouts.</summary>
    public static Rgb Mute(Rgb color)
    {
        var (h, s, l) = ToHsl(color.R, color.G, color.B);
        return FromHsl(h, Math.Min(s, 0.55), Math.Clamp(l, 0.45, 0.62));
    }

    internal static (double H, double S, double L) ToHsl(byte rb, byte gb, byte bb)
    {
        double r = rb / 255.0, g = gb / 255.0, b = bb / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = ((g - b) / d) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = ((b - r) / d) + 2;
        }
        else
        {
            h = ((r - g) / d) + 4;
        }

        return (h / 6, s, l);
    }

    internal static Rgb FromHsl(double h, double s, double l)
    {
        if (s < 1e-9)
        {
            var v = (byte)Math.Round(l * 255);
            return new Rgb(v, v, v);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        return new Rgb(Channel(p, q, h + (1.0 / 3)), Channel(p, q, h), Channel(p, q, h - (1.0 / 3)));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        var v = t < 1.0 / 6 ? p + ((q - p) * 6 * t)
            : t < 0.5 ? q
            : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6)
            : p;
        return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }
}

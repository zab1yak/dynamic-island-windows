using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Tests.UI;

/// <summary>Real Windows/WPF font resolution and raster output; never creates an Application.</summary>
public sealed class TypographyRenderingTests
{
    private const string RequiredGlyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyzАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя0123456789₽€%°«»“”‘’";
    private static readonly int[] Scales = [70, 80, 90, 95, 100, 110, 130];
    private static readonly int[] Dpis = [96, 120, 144, 192];
    private static readonly bool[] Backgrounds = [true, false];
    private static readonly FontWeight[] Weights = [FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Family(string Name, FontFamily Font, string? ExpectedUri);
    private sealed record Sample(string Role, string Text, double Size, FontWeight Weight, double Height);
    private sealed record GlyphEvidence(string Uri, int Weight, string Simulations);
    private sealed record SampleEvidence(string Family, string Role, double Width, double Height, IReadOnlyList<GlyphEvidence> Faces);
    private sealed record BoardEvidence(int Scale, int Dpi, string Background, IReadOnlyList<SampleEvidence> Samples);

    [Fact]
    public void Embedded_candidates_resolve_real_static_faces_and_cover_Russian_English_and_symbols()
    {
        OnSta(() =>
        {
            foreach (var family in Families().Where(family => family.ExpectedUri is not null))
            {
                foreach (var weight in Weights)
                {
                    var typeface = new Typeface(family.Font, FontStyles.Normal, weight, FontStretches.Normal);
                    Assert.True(typeface.TryGetGlyphTypeface(out var glyph), $"Could not resolve {family.Name} {weight}");
                    Assert.Contains(family.ExpectedUri!, glyph.FontUri.ToString(), StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(weight.ToOpenTypeWeight(), glyph.Weight.ToOpenTypeWeight());
                    Assert.Equal(StyleSimulations.None, glyph.StyleSimulations);
                    foreach (var character in RequiredGlyphs)
                    {
                        Assert.True(glyph.CharacterToGlyphMap.TryGetValue(character, out var index) && index != 0,
                            $"{family.Name} {weight}: missing U+{(int)character:X4}");
                    }
                }
            }
        });
    }

    [Fact]
    public void Tabular_timer_digits_keep_identical_widths_in_embedded_Inter()
    {
        OnSta(() =>
        {
            var family = Families().Single(family => family.Name == "Inter");
            var widths = "0123456789".Select(digit =>
            {
                var text = new TextBlock
                {
                    FontFamily = family.Font,
                    FontSize = 40,
                    FontWeight = FontWeights.SemiBold,
                    Text = new string(digit, 4) + ":" + new string(digit, 2),
                };
                Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return text.DesiredSize.Width;
            }).ToArray();
            Assert.True(widths.Max() - widths.Min() < 0.01, $"Timer advances differ: {string.Join(", ", widths)}");
        });
    }

    [Fact]
    public void Compare_actual_Wpf_text_at_all_requested_scales_Dpis_and_backgrounds()
    {
        OnSta(() =>
        {
            var output = Environment.GetEnvironmentVariable("DI_TYPOGRAPHY_SCREENSHOTS");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
            }

            var records = new List<BoardEvidence>();
            foreach (var scale in Scales)
            {
                foreach (var dpi in Dpis)
                {
                    foreach (var dark in Backgrounds)
                    {
                        var visual = new DrawingVisual();
                        var evidence = new List<SampleEvidence>();
                        var factor = scale / 100d;
                        const double width = 1440;
                        const double height = 440;
                        using (var drawing = visual.RenderOpen())
                        {
                            drawing.PushTransform(new ScaleTransform(factor, factor));
                            drawing.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(20, 20, 22) : Color.FromRgb(232, 232, 236)), null, new Rect(0, 0, width, height));
                            var column = 0;
                            foreach (var family in Families())
                            {
                                var y = 12d;
                                DrawSample(drawing, family, new Sample("candidate", family.Name, 15, FontWeights.SemiBold, 30), column * 480 + 12, y, dark, dpi, evidence);
                                y += 30;
                                foreach (var sample in Samples())
                                {
                                    DrawSample(drawing, family, sample, column * 480 + 12, y, dark, dpi, evidence);
                                    y += sample.Height;
                                }
                                Assert.True(y <= height, $"Board rows exceed {height} DIPs");
                                column++;
                            }
                            drawing.Pop();
                        }

                        var bitmap = new RenderTargetBitmap(
                            (int)Math.Ceiling(width * factor * dpi / 96d),
                            (int)Math.Ceiling(height * factor * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
                        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
                        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
                        bitmap.Render(visual);
                        Assert.True(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0);
                        var background = dark ? "dark" : "light";
                        records.Add(new BoardEvidence(scale, dpi, background, evidence));
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(output, $"typography-{scale}pct-{dpi}dpi-{background}.png"));
                            encoder.Save(stream);
                        }
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(output))
            {
                File.WriteAllText(Path.Combine(output, "wpf-font-evidence.json"), JsonSerializer.Serialize(records, JsonOptions));
            }
        });
    }

    private static Family[] Families()
    {
        // Loading a resource dictionary is safe even after the layout suite has shut down its Application.
        var typography = (ResourceDictionary)Application.LoadComponent(new Uri("/DynamicIsland.UI;component/Themes/Typography.xaml", UriKind.Relative));
        return
        [
            new("Previous machine fallback", new FontFamily("SF Pro Text, SF Pro Display, Segoe UI Variable Text, Segoe UI"), null),
            new("Inter", (FontFamily)typography["Font.Text"], "DynamicIsland.UI;component/Fonts/Inter/"),
            new("Source Sans 3", new FontFamily("pack://application:,,,/DynamicIsland.Tests;component/Fixtures/Fonts/SourceSans3/#Source Sans 3"), "DynamicIsland.Tests;component/Fixtures/Fonts/SourceSans3/"),
        ];
    }

    private static Sample[] Samples() =>
    [
        new("page title RU", "Таймер · Напоминание о воде", 15, FontWeights.Bold, 26),
        new("body RU", "Съешь ещё этих мягких французских булок.", 13, FontWeights.Normal, 22),
        new("body EN", "The quick brown fox jumps over the lazy dog.", 13, FontWeights.Normal, 22),
        new("secondary RU/EN", "Запись сохранена · Recording saved", 12, FontWeights.Normal, 22),
        new("caption RU/EN", "Следующий перерыв · Next break", 11, FontWeights.Normal, 20),
        new("button RU/EN", "Добавить задачу · Add task", 12, FontWeights.Medium, 22),
        new("editor RU/EN", "Введите текст для перевода.\nEnter text to translate.", 14, FontWeights.Normal, 44),
        new("compact", "09:41 · 100% · 23°", 13, FontWeights.SemiBold, 22),
        new("medium number", "0.3 / 2.0 L", 22, FontWeights.Bold, 34),
        new("timer", "25:00  11:11  88:88", 40, FontWeights.SemiBold, 56),
        new("glyphs", "ё Ё ₽ € % ° «кавычки» “quotes” ‘text’", 13, FontWeights.Normal, 22),
        new("weights", "Regular 400 · Medium 500 · Semibold 600 · Bold 700", 13, FontWeights.Normal, 22),
    ];

    private static void DrawSample(DrawingContext destination, Family family, Sample sample, double x, double y,
        bool dark, int dpi, List<SampleEvidence> evidence)
    {
        var brush = dark ? Brushes.WhiteSmoke : Brushes.Black;
        var text = new FormattedText(sample.Text, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight,
            new Typeface(family.Font, FontStyles.Normal, sample.Weight, FontStretches.Normal), sample.Size, brush,
            null, TextFormattingMode.Display, dpi / 96d);
        Assert.True(text.WidthIncludingTrailingWhitespace <= 456, $"{family.Name}/{sample.Role} exceeds column: {text.WidthIncludingTrailingWhitespace}");
        Assert.True(text.Height <= sample.Height, $"{family.Name}/{sample.Role} exceeds line box: {text.Height}");
        var sampleVisual = new DrawingVisual();
        using (var drawing = sampleVisual.RenderOpen())
        {
            drawing.DrawText(text, new Point(0, 0));
        }
        var faces = GlyphRuns(sampleVisual.Drawing).Select(run => new GlyphEvidence(run.GlyphTypeface.FontUri.ToString(),
            run.GlyphTypeface.Weight.ToOpenTypeWeight(), run.GlyphTypeface.StyleSimulations.ToString())).Distinct().ToArray();
        Assert.NotEmpty(faces);
        if (family.ExpectedUri is not null)
        {
            foreach (var face in faces)
            {
                Assert.Contains(family.ExpectedUri, face.Uri, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(sample.Weight.ToOpenTypeWeight(), face.Weight);
                Assert.Equal(nameof(StyleSimulations.None), face.Simulations);
            }
        }
        evidence.Add(new SampleEvidence(family.Name, sample.Role, text.WidthIncludingTrailingWhitespace, text.Height, faces));
        destination.PushTransform(new TranslateTransform(x, y));
        destination.DrawDrawing(sampleVisual.Drawing);
        destination.Pop();
    }

    private static IEnumerable<GlyphRun> GlyphRuns(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph)
        {
            yield return glyph.GlyphRun;
        }
        else if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var run in GlyphRuns(child))
                {
                    yield return run;
                }
            }
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("STA typography rendering failed", failure);
        }
    }
}

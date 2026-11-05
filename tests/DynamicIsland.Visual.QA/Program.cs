using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.App.Island;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Settings;
using DynamicIsland.Platform.Windows.Status;
using DynamicIsland.UI.Controls;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using WpfPath = System.Windows.Shapes.Path;

namespace DynamicIsland.Visual.QA;

internal static class Program
{
    private static readonly int[] Scales = [70, 80, 90, 95, 100, 110, 130];
    private static readonly int[] Dpis = [96, 120, 144, 192];
    private static readonly string[] Languages = ["ru", "en"];
    private static readonly bool[] Backdrops = [false, true];
    private static readonly IslandBackground[] Themes = [IslandBackground.Black, IslandBackground.Translucent, IslandBackground.LightGray];
    private static readonly string[] CardKeys = ["Brush.Surface1", "Brush.Surface2", "Brush.Surface3"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static int _exitCode;

    [STAThread]
    public static int Main(string[] args)
    {
        var outputIndex = Array.IndexOf(args, "--output");
        var output = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1]
            : Environment.GetEnvironmentVariable("DI_VISUAL_QA_ARTIFACTS") ?? System.IO.Path.Combine("artifacts", "visual-qa");
        output = System.IO.Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var quick = args.Contains("--quick", StringComparer.Ordinal);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, error) =>
        {
            _exitCode = 1;
            File.WriteAllText(System.IO.Path.Combine(output, "qa-dispatcher-failure.txt"), error.Exception.ToString());
            error.Handled = true;
            app.Shutdown(1);
        };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DynamicIsland.UI;component/Themes/Theme.xaml"),
        });
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await RunAsync(output, quick);
            }
            catch (Exception exception)
            {
                _exitCode = 1;
                File.WriteAllText(System.IO.Path.Combine(output, "qa-failure.txt"), exception.ToString());
                Console.Error.WriteLine(exception);
            }
            finally
            {
                app.Shutdown(_exitCode);
            }
        });
        app.Run();
        return _exitCode;
    }

    private static async Task RunAsync(string output, bool quick)
    {
        var settings = new MemorySettings();
        var fixtures = Fixtures.CreateModules();
        Require(Loc.Instance["Todo.Add"] != "Todo.Add", "Production module localization did not resolve");
        var host = new ModuleHost(fixtures, [], settings, NullLogger<ModuleHost>.Instance);
        await host.InitializeAsync(CancellationToken.None);
        using var state = new IslandStateMachine(TimeProvider.System, new QaDispatcher());
        var live = new LiveActivityHost(fixtures, TimeProvider.System, state, host);
        using var browser = new ForegroundBrowserWatcher(); // Deliberately never started.
        var status = new StatusViewModel(new InertBattery(), new InertPrivacy(), browser)
        {
            BatteryStatus = new(true, 76, false, false), BatteryText = "76%", BrowserTitle = "Synthetic QA browser tab",
        };
        using var viewModel = new IslandViewModel(TimeProvider.System, status, live, host, []) { Time24 = "09:41" };
        var window = new IslandWindow(state, settings, viewModel, new IslandRenderStats(), NullLogger<IslandWindow>.Instance);
        var metrics = new List<FrameEvidence>();
        var timed = new List<TimedEvidence>();
        try
        {
            window.Show();
            await PumpAsync(450);
            await CheckTimedAlphaAsync(window, state, settings, output, timed);
            var ordinal = 0;
            foreach (var language in Languages)
            {
                Loc.Instance.SetLanguage(language);
                Fixtures.RefreshLanguage(fixtures);
                viewModel.Time24 = "09:41";
                viewModel.DateText = language == "ru" ? "Чт, 5 ноября" : "Thu, 5 November";
                foreach (var theme in Themes)
                {
                    settings.Current.Background = theme;
                    await settings.SaveAsync();
                    await PumpAsync(400);
                    state.Dismiss();
                    state.HotkeyPressed();
                    await PumpAsync(1200);
                    CheckSurfaceScopes(window, theme);
                    foreach (var module in fixtures)
                    {
                        host.ShowModule(module.Id);
                        await PumpAsync(500);
                        for (var scaleIndex = 0; scaleIndex < Scales.Length; scaleIndex++)
                        {
                            var scale = Scales[scaleIndex];
                            settings.Current.IslandScale = scale / 100d;
                            await settings.SaveAsync();
                            await PumpAsync(40);
                            window.UpdateLayout();
                            var structural = Inspect(window, theme);
                            for (var dpiIndex = 0; dpiIndex < Dpis.Length; dpiIndex++)
                            {
                                // Cover every scale and DPI for every page/theme/language in quick mode.
                                // The full mode produces the complete Cartesian matrix.
                                if (quick && dpiIndex != (scaleIndex + module.Order) % Dpis.Length) continue;
                                var dpi = Dpis[dpiIndex];
                                var source = Capture(window, dpi);
                                foreach (var darkBackdrop in Backdrops)
                                {
                                    var backdrop = darkBackdrop ? "dark" : "light";
                                    var name = $"{language}-{theme}-{module.Id}-{scale}pct-{dpi}bitmapdpi-{backdrop}.png";
                                    Save(Composite(source, darkBackdrop), System.IO.Path.Combine(output, name));
                                    metrics.Add(new FrameEvidence(name, language, theme.ToString(), module.Id, scale, dpi,
                                        backdrop, structural, source.PixelWidth, source.PixelHeight));
                                    ordinal++;
                                }
                            }
                        }
                        Console.WriteLine($"QA {language}/{theme}/{module.Id}: {ordinal} images");
                    }
                }
            }
            Require(metrics.Count == (quick ? 588 : 2352), $"Unexpected matrix size: {metrics.Count}");
            WriteJson(output, "visual-qa-metrics.json", new
            {
                Description = "Actual production IslandWindow and production templates with synthetic bindings; WPF bitmap DPI, not physical monitor DPI",
                Quick = quick,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                ActualWindowDpi = VisualTreeHelper.GetDpi(window).PixelsPerInchX,
                Frames = metrics,
                TimedAlpha = timed,
            });
            Console.WriteLine($"PASS: {metrics.Count} production-window images; {timed.Count} timed alpha observations. {output}");
        }
        finally
        {
            // Keep partial evidence even if an assertion fails.
            WriteJson(output, "timed-alpha.json", timed);
            WriteJson(output, "completed-frames.json", metrics);
            window.CloseForExit();
        }
    }

    private static async Task CheckTimedAlphaAsync(IslandWindow window, IslandStateMachine state, MemorySettings settings,
        string output, List<TimedEvidence> evidence)
    {
        settings.Current.Background = IslandBackground.Translucent;
        settings.Current.IslandScale = 1;
        await settings.SaveAsync();
        state.Dismiss();
        await PumpAsync(1300);
        var body = (WpfPath)window.FindName("Body");
        var sample = new Point(IslandMetrics.WindowWidth / 2, 32);
        RecordTimed(window, body, sample, "compact", 0, output, evidence);
        state.HotkeyPressed();
        var elapsed = Stopwatch.StartNew();
        RecordTimed(window, body, sample, "expand-request", elapsed.Elapsed.TotalMilliseconds, output, evidence);
        foreach (var target in new[] { 50, 150, 350, 700, 1200 })
        {
            await PumpAsync(Math.Max(1, target - (int)elapsed.ElapsedMilliseconds));
            RecordTimed(window, body, sample, target == 1200 ? "settled" : $"morph-{target}ms", elapsed.Elapsed.TotalMilliseconds, output, evidence);
        }
        await PumpAsync(1000);
        RecordTimed(window, body, sample, "settled-plus-1s", elapsed.Elapsed.TotalMilliseconds, output, evidence);
        await PumpAsync(2000);
        RecordTimed(window, body, sample, "settled-plus-3s", elapsed.Elapsed.TotalMilliseconds, output, evidence);
        Require(body.Effect is null, "Translucent Body.Effect must remain null after settling");
        foreach (var item in evidence)
        {
            Require(Math.Abs(item.Alpha - 192) <= 2, $"Translucent alpha changed at {item.Stage}: {item.Alpha}");
            Require(item.Effect is null, $"Translucent Body acquired {item.Effect} at {item.Stage}");
        }
        var fixedFrame = Capture(window, 144);
        // Controlled causal reproduction of the old parameters on the CURRENT body, not the old binary.
        body.Effect = new DropShadowEffect
        {
            Color = Colors.Black, Direction = 270, ShadowDepth = 3, BlurRadius = 20,
            Opacity = 0.45, RenderingBias = RenderingBias.Performance,
        };
        window.UpdateLayout();
        await PumpAsync(50);
        var controlledFrame = Capture(window, 144);
        var oldAlpha = AlphaAt(controlledFrame, sample, 144);
        if (oldAlpha <= 192 + 8)
        {
            // Some hosted renderers do not materialize WPF bitmap effects. Give a
            // late compositor one more turn, then record an inconclusive control.
            await PumpAsync(500);
            controlledFrame = Capture(window, 144);
            oldAlpha = AlphaAt(controlledFrame, sample, 144);
        }
        var reproduced = oldAlpha > 192 + 8;
        Console.WriteLine($"Controlled prior-effect diagnostic: delta rendered={reproduced}; alpha={oldAlpha}; tier={RenderCapability.Tier >> 16}");
        SaveComparison(fixedFrame, controlledFrame, System.IO.Path.Combine(output, "controlled-v031-effect-vs-current-fixed.png"));
        WriteJson(output, "controlled-effect-evidence.json", new
        {
            Description = "Controlled prior v0.3.1 DropShadowEffect parameters on CURRENT production Body, not an old binary screenshot",
            FixedAlpha = AlphaAt(fixedFrame, sample, 144), ControlledPriorEffectAlpha = oldAlpha,
            Reproduced = reproduced, RenderingTier = RenderCapability.Tier >> 16,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            Limitation = reproduced ? null : "Hosted renderer did not materialize a measurable bitmap-effect alpha delta; causal control is inconclusive on this runner",
            Sample = new { sample.X, sample.Y }, BitmapDpi = 144,
        });
        body.Effect = null;
        foreach (var theme in new[] { IslandBackground.Black, IslandBackground.LightGray })
        {
            settings.Current.Background = theme;
            await settings.SaveAsync();
            await PumpAsync(1400);
            Require(body.Effect is DropShadowEffect { Opacity: > 0.4 }, $"{theme} must retain its settled shadow");
            Save(Capture(window, 144), System.IO.Path.Combine(output, $"settled-shadow-retained-{theme}.png"));
        }
    }

    private static void RecordTimed(IslandWindow window, WpfPath body, Point sample, string stage, double milliseconds,
        string output, List<TimedEvidence> evidence)
    {
        var frame = Capture(window, 96);
        var alpha = AlphaAt(frame, sample, 96);
        evidence.Add(new TimedEvidence(stage, milliseconds, alpha, body.Effect?.GetType().Name,
            body.Data.Bounds.Width, body.Data.Bounds.Height));
        Save(frame, System.IO.Path.Combine(output, $"timed-alpha-{stage}.png"));
    }

    private static StructuralEvidence Inspect(IslandWindow window, IslandBackground theme)
    {
        var title = (TextBlock)window.FindName("PageTitle");
        Require(!title.IsVisible || !string.IsNullOrWhiteSpace(title.Text), "Visible production page title is empty");
        var root = (FrameworkElement)window.FindName("Root");
        var body = (WpfPath)window.FindName("Body");
        var outside = new Point(1, root.ActualHeight - 1);
        Require(!body.Data.FillContains(outside), "Chosen outside sample unexpectedly lies inside body geometry");
        var hit = root.InputHitTest(outside);
        Require(hit is null, $"Outside geometry caught input: {hit?.GetType().FullName}");
        var faces = Descendants(window).OfType<System.Windows.Media.Visual>()
            .SelectMany(visual => GlyphRuns(VisualTreeHelper.GetDrawing(visual)))
            .Select(run => new FontEvidence(run.GlyphTypeface.FontUri.ToString(), run.GlyphTypeface.Weight.ToOpenTypeWeight(), run.GlyphTypeface.StyleSimulations.ToString()))
            .Distinct().ToArray();
        Require(faces.Length > 0, "No actual rendered GlyphRunDrawing font evidence found");
        foreach (var face in faces)
        {
            Require(face.Uri.Contains("DynamicIsland.UI;component/Fonts/Inter/", StringComparison.OrdinalIgnoreCase), $"Unexpected font fallback: {face.Uri}");
            Require(face.Simulations == nameof(StyleSimulations.None), $"Synthetic font face: {face}");
        }
        var cards = Descendants(window).OfType<SquircleBorder>().Where(card => card.IsVisible && card.Background is SolidColorBrush)
            .Select(card => new CardEvidence(((SolidColorBrush)card.Background!).Color.ToString(CultureInfo.InvariantCulture), card.ActualWidth, card.ActualHeight)).ToArray();
        Require(theme != IslandBackground.Translucent || body.Effect is null, "Translucent shadow returned during matrix");
        return new StructuralEvidence(new PointEvidence(outside.X, outside.Y), hit?.GetType().FullName,
            ((SolidColorBrush)body.Fill).Color.ToString(CultureInfo.InvariantCulture), body.Effect?.GetType().Name, faces, cards);
    }

    private static void CheckSurfaceScopes(IslandWindow window, IslandBackground theme)
    {
        foreach (var key in CardKeys)
        {
            var appBrush = (SolidColorBrush)Application.Current.FindResource(key);
            var islandBrush = (SolidColorBrush)window.FindResource(key);
            Require(appBrush.Color.A == 255, $"Global/settings card {key} must stay opaque: {appBrush.Color}");
            Require(theme == IslandBackground.Translucent ? islandBrush.Color.A < 255 : islandBrush.Color.A == 255,
                $"Unexpected island card alpha for {theme}/{key}: {islandBrush.Color}");
        }
    }

    private static RenderTargetBitmap Capture(IslandWindow window, int dpi)
    {
        window.UpdateLayout();
        var frame = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi / 96d),
            (int)Math.Ceiling(window.ActualHeight * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        frame.Render(window);
        return frame;
    }

    private static byte AlphaAt(BitmapSource image, Point sample, int dpi)
    {
        var bytes = new byte[4];
        image.CopyPixels(new Int32Rect((int)Math.Round(sample.X * dpi / 96d), (int)Math.Round(sample.Y * dpi / 96d), 1, 1), bytes, 4, 0);
        return bytes[3];
    }

    private static RenderTargetBitmap Composite(BitmapSource source, bool dark)
    {
        var width = source.PixelWidth * 96d / source.DpiX;
        var height = source.PixelHeight * 96d / source.DpiY;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(45, 52, 66) : Color.FromRgb(235, 238, 244)), null, new Rect(0, 0, width, height));
            // The band is a fixed backdrop detail, visible only through translucent surfaces.
            drawing.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(67, 85, 104) : Color.FromRgb(186, 206, 222)), null, new Rect(width * 0.45, 0, width * 0.20, height));
            drawing.DrawImage(source, new Rect(0, 0, width, height));
        }
        var result = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
        result.Render(visual);
        return result;
    }

    private static void SaveComparison(BitmapSource fixedFrame, BitmapSource controlledFrame, string path)
    {
        var width = fixedFrame.PixelWidth * 96d / fixedFrame.DpiX;
        var height = fixedFrame.PixelHeight * 96d / fixedFrame.DpiY;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width * 2, height + 54));
            drawing.DrawImage(Composite(fixedFrame, false), new Rect(0, 0, width, height));
            drawing.DrawImage(Composite(controlledFrame, false), new Rect(width, 0, width, height));
            var family = (FontFamily)Application.Current.FindResource("Font.Text");
            DrawLabel(drawing, family, "CURRENT production window: fixed translucent body", new Point(12, height + 8));
            DrawLabel(drawing, family, "CONTROLLED v0.3.1 shadow parameters on current Body", new Point(width + 12, height + 8));
            DrawLabel(drawing, family, "Causal reproduction only; this is not the old installed binary.", new Point(width + 12, height + 28));
        }
        var result = new RenderTargetBitmap((int)Math.Ceiling(width * 2 * 1.5), (int)Math.Ceiling((height + 54) * 1.5), 144, 144, PixelFormats.Pbgra32);
        result.Render(visual);
        Save(result, path);
    }

    private static void DrawLabel(DrawingContext drawing, FontFamily family, string text, Point point) =>
        drawing.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 12, Brushes.Black, 1.5), point);

    private static void Save(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static async Task PumpAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
    }

    private static IEnumerable<GlyphRun> GlyphRuns(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
        else if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var run in GlyphRuns(child)) yield return run;
            }
        }
    }

    private static void WriteJson<T>(string output, string name, T value) =>
        File.WriteAllText(System.IO.Path.Combine(output, name), JsonSerializer.Serialize(value, JsonOptions));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record FontEvidence(string Uri, int Weight, string Simulations);
    private sealed record CardEvidence(string Color, double Width, double Height);
    private sealed record PointEvidence(double X, double Y);
    private sealed record StructuralEvidence(PointEvidence OutsidePoint, string? OutsideHit, string BodyColor, string? BodyEffect, IReadOnlyList<FontEvidence> Fonts, IReadOnlyList<CardEvidence> Cards);
    private sealed record FrameEvidence(string File, string Language, string Theme, string Page, int Scale, int BitmapDpi, string Backdrop, StructuralEvidence Structure, int PixelWidth, int PixelHeight);
    private sealed record TimedEvidence(string Stage, double ElapsedMilliseconds, byte Alpha, string? Effect, double BodyWidth, double BodyHeight);
}

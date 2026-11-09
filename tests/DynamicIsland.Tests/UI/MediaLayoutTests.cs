using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.Modules.Media;
using DynamicIsland.Modules.Media.Services;
using DynamicIsland.Modules.Media.ViewModels;
using DynamicIsland.Tests.Media;
using DynamicIsland.UI.Controls;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.UI;

/// <summary>Production music templates with real media view models and isolated session fixtures; no live GSMTC claim.</summary>
[Collection("Translator WPF localization")]
public sealed class MediaLayoutTests
{
    private static readonly int[] Scales = [70, 80, 90, 95, 100, 110, 130];
    private static readonly string[] Languages = ["ru", "en"];
    private static readonly string[] States = ["playing", "paused", "no-session", "long-title", "no-artwork", "unknown-duration", "unsupported"];

    [Fact]
    public void Reference_music_composition_and_live_pill_fit_existing_page_at_all_requested_scales()
    {
        OnSta(() =>
        {
            var previousLanguage = Loc.Instance.Culture.TwoLetterISOLanguageName;
            var theme = (ResourceDictionary)Application.LoadComponent(new Uri("/DynamicIsland.UI;component/Themes/Theme.xaml", UriKind.Relative));
            var media = (ResourceDictionary)Application.LoadComponent(new Uri("/DynamicIsland.Modules.Media;component/Views/MediaTemplates.xaml", UriKind.Relative));
            media.MergedDictionaries.Add(theme);
            Loc.Instance.Register(new ResourceManager("DynamicIsland.Modules.Media.Resources.Strings", typeof(MediaModule).Assembly));
            try
            {
                foreach (var language in Languages)
                {
                    Loc.Instance.SetLanguage(language);
                    foreach (var state in States)
                    {
                        CheckScenario(media, language, state);
                    }
                }
            }
            finally { Loc.Instance.SetLanguage(previousLanguage); }
        });
    }

    private static void CheckScenario(ResourceDictionary media, string language, string state)
    {
        var time = new FakeTimeProvider(Snapshots.T0);
        var source = new FakeMediaSource();
        using var island = new IslandStateMachine(time, new InlineDispatcher());
        using var model = new MediaViewModel(source, new SilentAudio(), time, new InlineDispatcher(), island, _ => Task.FromResult(new Rgb(80, 90, 110)));
        if (state != "no-session")
        {
            source.List.Add(Snapshots.Track(state: state == "playing" ? PlaybackState.Playing : PlaybackState.Paused,
                durationSeconds: state == "unknown-duration" ? 0 : 308, positionSeconds: 115,
                canSeek: state is not "unknown-duration" and not "unsupported") with
            {
                Title = state == "long-title" ? language == "ru" ? "Очень длинное название композиции, которое должно оставаться в своей области" : "A very long track title that must stay inside its own allocated region" : "Life",
                Artist = language == "ru" ? "Zivert, Оhne Eре — тестовый исполнитель" : "Zivert, Ohne Ere",
                Artwork = state == "no-artwork" ? null : Artwork(),
                CanNext = state != "unsupported", CanPrevious = state != "unsupported",
                CanRepeat = state != "unsupported", RepeatMode = state == "unsupported" ? null : MediaRepeatMode.List,
                CanShuffle = state != "unsupported", IsShuffleActive = state == "unsupported" ? null : false,
            });
            source.List.Add(Snapshots.Track("second-player", PlaybackState.Paused));
            source.Raise();
        }

        var template = (DataTemplate)media[new DataTemplateKey(typeof(MediaViewModel))];
        var page = new ContentPresenter { Content = model, ContentTemplate = template };
        page.Resources.MergedDictionaries.Add(media);
        var root = new Border { Child = page, Background = Brushes.Black, UseLayoutRounding = true };
        root.SetValue(TextElement.ForegroundProperty, Brushes.White);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
        try
        {
            foreach (var percent in Scales)
            {
                var scale = percent / 100d;
                page.Width = 484;
                page.Height = 104;
                page.LayoutTransform = new ScaleTransform(scale, scale);
                var size = new Size(484 * scale, 104 * scale);
                Arrange(root, size);
                var scenario = $"{language}/{state}/{percent}%";
                var track = Find<Grid>(template, page, "TrackView");
                Assert.Equal(state == "no-session" ? Visibility.Collapsed : Visibility.Visible, track.Visibility);
                if (state == "no-session")
                {
                    Assert.Equal(Visibility.Visible, Find<StackPanel>(template, page, "EmptyState").Visibility);
                }
                else
                {
                    var artwork = Bounds(Find<Grid>(template, page, "ArtworkHost"), root);
                    var details = Bounds(Find<StackPanel>(template, page, "TrackDetails"), root);
                    var title = Bounds(Find<MarqueeText>(template, page, "TrackTitle"), root);
                    var transport = Find<StackPanel>(template, page, "Transport");
                    var transportBounds = Bounds(transport, root);
                    var seek = Find<Scrubber>(template, page, "SeekBar");
                    var seekBounds = Bounds(seek, root);
                    var secondary = Bounds(Find<StackPanel>(template, page, "SecondaryActions"), root);
                    Assert.InRange(artwork.Width / scale, 83, 85);
                    Assert.InRange(artwork.Height / scale, 83, 85);
                    Assert.True(artwork.Right <= details.Left + 1 && details.Right <= transportBounds.Left + 1, $"{scenario}: cover/text/transport overlap");
                    Assert.True(seekBounds.Top >= title.Bottom, $"{scenario}: progress must be below the title");
                    Assert.True(seekBounds.Left >= details.Left - 1 && seekBounds.Right <= details.Right + 1, $"{scenario}: progress escapes text region");
                    Assert.True(secondary.Top >= transportBounds.Bottom - 1, $"{scenario}: secondary actions overlap transport");
                    Assert.Equal(3, transport.Children.OfType<Button>().Count());
                    Assert.Equal(source.List[0].CanSeek, seek.IsInteractive);
                    if (state == "unsupported")
                    {
                        Assert.False(((Button)transport.Children[0]).IsEnabled);
                        Assert.False(((Button)transport.Children[2]).IsEnabled);
                        var actions = Find<StackPanel>(template, page, "SecondaryActions").Children.OfType<Button>().ToArray();
                        Assert.False(actions[1].IsEnabled);
                        Assert.False(actions[2].IsEnabled);
                        Assert.Equal(model.ShuffleLabel, actions[1].ToolTip);
                        Assert.Equal(model.RepeatLabel, actions[2].ToolTip);
                    }
                    Assert.Equal(model.DurationText, Find<TextBlock>(template, page, "Duration").Text);
                    Assert.Equal(state == "unknown-duration" ? "0:00" : "5:08", model.DurationText);
                    foreach (var element in Descendants<FrameworkElement>(track).Where(element => element is Button or MarqueeText or Scrubber))
                    {
                        if (!EffectivelyVisible(element, root)) continue;
                        var bounds = Bounds(element, root);
                        Assert.True(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
                            bounds.Right <= size.Width + 1 && bounds.Bottom <= size.Height + 1, $"{scenario}: {element.GetType().Name} escapes page: {bounds}");
                    }
                    // Exercise actual bound transport/secondary commands without changing production session handling.
                    if (percent == 100 && state != "unsupported")
                    {
                        ((Button)transport.Children[0]).Command.Execute(null);
                        ((Button)transport.Children[1]).Command.Execute(null);
                        ((Button)transport.Children[2]).Command.Execute(null);
                        Assert.Contains("prev Spotify.exe", source.Commands);
                        Assert.Contains(state == "playing" ? "pause Spotify.exe" : "play Spotify.exe", source.Commands);
                        Assert.Contains("next Spotify.exe", source.Commands);
                        var actions = Find<StackPanel>(template, page, "SecondaryActions").Children.OfType<Button>().ToArray();
                        actions[1].Command.Execute(null);
                        actions[2].Command.Execute(null);
                        Assert.Contains("shuffle Spotify.exe True", source.Commands);
                        Assert.Contains("repeat Spotify.exe Track", source.Commands);
                        if (seek.IsInteractive)
                        {
                            seek.SeekCommand!.Execute(0.5);
                            Assert.Contains("seek Spotify.exe 154", source.Commands);
                        }
                        // Restore the synthetic snapshot before saving comparable scale images.
                        source.Raise();
                    }
                }
                Save(root, size, $"media-page-{language}-{state}-{percent}.png");
            }
            // The live pill stays the existing 34-DIP template, not a second redesigned page.
            var liveTemplate = (DataTemplate)media[new DataTemplateKey(typeof(MediaLiveViewModel))];
            page.Content = model.Live;
            page.ContentTemplate = liveTemplate;
            page.Width = 240;
            page.Height = 34;
            page.LayoutTransform = Transform.Identity;
            Arrange(root, new Size(240, 34));
            Save(root, new Size(240, 34), $"media-live-{language}-{state}.png");
        }
        finally
        {
            page.Content = null;
            page.ContentTemplate = null;
            root.Child = null;
        }
    }

    private static T Find<T>(DataTemplate template, ContentPresenter page, string name) where T : FrameworkElement =>
        Assert.IsType<T>(template.FindName(name, page));

    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static bool EffectivelyVisible(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(current, root)) return true;
        }
        return false;
    }

    private static void Arrange(FrameworkElement root, Size size)
    {
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
    }

    private static void Save(Visual root, Size size, string fileName)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var directory = Environment.GetEnvironmentVariable("DI_MEDIA_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }

    private static byte[] Artwork()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.SlateGray, null, new Rect(0, 0, 84, 84));
            drawing.DrawEllipse(Brushes.LightSteelBlue, null, new Point(42, 42), 26, 26);
        }
        var bitmap = new RenderTargetBitmap(84, 84, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "Music WPF layout test timed out");
        if (failure is not null) throw new InvalidOperationException("Music WPF layout/render regression failed", failure);
    }
}

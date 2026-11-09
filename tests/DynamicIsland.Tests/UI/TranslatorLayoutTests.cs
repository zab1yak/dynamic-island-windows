using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Translator;
using DynamicIsland.Modules.Translator.Services;
using DynamicIsland.Modules.Translator.Views;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Tests.UI;

[CollectionDefinition("Translator WPF localization", DisableParallelization = true)]
public sealed class TranslatorLayoutIsolation;

/// <summary>Real WPF layout/render checks with isolated, network-free services. These are not interactive GUI acceptance.</summary>
[Collection("Translator WPF localization")]
public sealed class TranslatorLayoutTests
{
    private static readonly int[] Scales = [70, 80, 90, 95, 100, 110, 130];
    private static readonly string[] Languages = ["ru", "en"];
    private static readonly string[] States = ["missing", "ready", "translating", "downloading", "error"];

    [Fact]
    public void Translator_controls_fit_the_actual_expanded_page_across_scales_languages_and_states()
    {
        OnSta(() =>
        {
            // Production pages resolve StaticResource references during InitializeComponent,
            // so give them the same Application resource scope used by the real app.
            var previousLanguage = Loc.Instance.Culture.TwoLetterISOLanguageName;
            var application = Application.Current;
            application.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri("/DynamicIsland.UI;component/Themes/Theme.xaml", UriKind.Relative)));
            Loc.Instance.Register(new ResourceManager("DynamicIsland.Modules.Translator.Resources.Strings", typeof(TranslatorModule).Assembly));
            try
            {
                foreach (var language in Languages)
                {
                    Loc.Instance.SetLanguage(language);
                    foreach (var scale in Scales)
                    {
                        foreach (var state in States)
                        {
                            CheckLayout(language, scale, state);
                        }
                    }
                }
            }
            finally
            {
                Loc.Instance.SetLanguage(previousLanguage);
            }
        });
    }

    private static void CheckLayout(string language, int percent, string state)
    {
        using var history = new TranslationHistoryService(new MemoryStorage(), TimeProvider.System);
        using var packages = new TranslationPackagesViewModel(new Packages(state != "missing"));
        using var vm = new TranslatorViewModel(new Service(state == "error"), new Host(), history, packages, TimeProvider.System);
        vm.Input = string.Join(' ', Enumerable.Repeat("Hello, привет! A longer input checks wrapping and scrolling.", 12));
        if (state is "ready" or "error")
        {
            vm.TranslateCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        }
        vm.IsBusy = state == "translating";
        packages.IsBusy = state == "downloading";
        packages.Progress = 45;
        var page = new TranslatorPage { DataContext = vm };
        var scale = percent / 100.0;
        // IslandWindow.ExpandedLayer: 580x160, header40, footer16;
        // PageHost has48 DIP margins on each side. Keep the real dimensions.
        var expanded = new Grid { Width = 580, Height = 160, LayoutTransform = new ScaleTransform(scale, scale) };
        expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        var presenter = new ContentPresenter { Content = page, Margin = new Thickness(48, 0, 48, 0) };
        Grid.SetRow(presenter, 1);
        expanded.Children.Add(presenter);
        var root = new Border { Child = expanded, Background = Brushes.Black, UseLayoutRounding = true };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
        var size = new Size(580 * scale, 160 * scale);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();

        var scenario = $"{language}/{percent}%/{state}";
        if (state == "downloading")
        {
            var status = Assert.IsType<TextBlock>(page.FindName("DownloadStatus"));
            var foreground = Assert.IsAssignableFrom<SolidColorBrush>(status.Foreground).Color;
            var background = Assert.IsAssignableFrom<SolidColorBrush>(page.FindResource("Brush.Surface1")).Color;
            Assert.True(Contrast(foreground, background) >= 4.5, $"{scenario}: download status has insufficient text contrast");
        }
        var pageBounds = Bounds(page, root);
        var controls = Descendants<Control>(page)
            .Where(control => control is ComboBox or TextBox or Button && control.Visibility == Visibility.Visible)
            .ToArray();
        Assert.Equal(2, controls.OfType<ComboBox>().Count());
        var fields = controls.OfType<TextBox>().OrderBy(field => Bounds(field, root).Left).ToArray();
        Assert.Equal(2, fields.Length);
        foreach (var control in controls)
        {
            var bounds = Bounds(control, root);
            Assert.True(bounds.Width > 0 && bounds.Height > 0, $"{scenario}: zero-sized {control.GetType().Name}");
            Assert.True(bounds.Left >= pageBounds.Left - 1 && bounds.Top >= pageBounds.Top - 1 &&
                bounds.Right <= pageBounds.Right + 1 && bounds.Bottom <= pageBounds.Bottom + 1,
                $"{scenario}: {control.GetType().Name} {bounds} escapes page {pageBounds}");
        }

        var leftField = Bounds(fields[0], root);
        var rightField = Bounds(fields[1], root);
        Assert.True(leftField.Right <= rightField.Left + 0.5, $"{scenario}: input and output overlap");
        var buttons = controls.OfType<Button>().ToArray();
        foreach (var button in buttons)
        {
            var bounds = Bounds(button, root);
            Assert.False(Overlaps(bounds, leftField) || Overlaps(bounds, rightField), $"{scenario}: button overlaps a text field");
        }
        for (var i = 0; i < buttons.Length; i++)
        {
            for (var j = i + 1; j < buttons.Length; j++)
            {
                Assert.False(Overlaps(Bounds(buttons[i], root), Bounds(buttons[j], root)), $"{scenario}: action buttons overlap");
            }
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); // Exercise the actual drawing paths in addition to Measure/Arrange.
        var artifacts = Environment.GetEnvironmentVariable("DI_LAYOUT_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(artifacts))
        {
            Directory.CreateDirectory(artifacts);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(artifacts, $"translator-{language}-{percent}-{state}.png"));
            encoder.Save(file);
        }
        page.DataContext = null;
        presenter.Content = null;
    }

    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static bool Overlaps(Rect a, Rect b) => a.Left < b.Right - 0.5 && a.Right > b.Left + 0.5 && a.Top < b.Bottom - 0.5 && a.Bottom > b.Top + 0.5;

    private static double Contrast(Color foreground, Color background)
    {
        var alpha = foreground.A / 255.0;
        static double Linear(double channel) => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        static double Luminance(double red, double green, double blue) => 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
        var back = Luminance(background.R / 255.0, background.G / 255.0, background.B / 255.0);
        var front = Luminance((foreground.R * alpha + background.R * (1 - alpha)) / 255.0,
            (foreground.G * alpha + background.G * (1 - alpha)) / 255.0,
            (foreground.B * alpha + background.B * (1 - alpha)) / 255.0);
        return (Math.Max(front, back) + 0.05) / (Math.Min(front, back) + 0.05);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) { yield return match; }
            foreach (var descendant in Descendants<T>(child)) { yield return descendant; }
        }
    }

    private static void OnSta(Action action) => LayoutTestApplication.Run(action);

    private sealed class Service(bool fail) : ITranslationService
    {
        public Task<TranslationResult> TranslateAsync(string text, string? sourceLanguage, string targetLanguage, CancellationToken cancellationToken) =>
            fail ? Task.FromException<TranslationResult>(new TranslationException(TranslationFailure.Unavailable)) :
                Task.FromResult(new TranslationResult(string.Join(' ', Enumerable.Repeat("Привет! A longer result checks scrolling.", 12)), sourceLanguage));
    }

    private sealed class Packages(bool installed) : ITranslationPackageManager
    {
        public IReadOnlyList<TranslationPackage> AvailablePackages => [];
        public bool IsInstalled(string sourceLanguage, string targetLanguage) => installed;
        public string GetInstalledPath(string sourceLanguage, string targetLanguage) => throw new InvalidOperationException("Layout tests do not load models.");
        public Task InstallAsync(string sourceLanguage, string targetLanguage, IProgress<TranslationPackageProgress>? progress, CancellationToken cancellationToken) => throw new InvalidOperationException("Layout tests do not download models.");
        public Task RemoveAsync(string sourceLanguage, string targetLanguage, CancellationToken cancellationToken) => throw new InvalidOperationException("Layout tests do not modify models.");
    }

    private sealed class MemoryStorage : IStorageService
    {
        public string RootPath => string.Empty;
        public string ResolvePath(string relativePath) => throw new InvalidOperationException("Layout tests do not access user files.");
        public Task<T?> LoadAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class => Task.FromResult<T?>(null);
        public Task SaveAsync<T>(string relativePath, T value, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    }

    private sealed class Host : IIslandHost
    {
        public void RequestKeyboard() { }
        public void ReleaseKeyboard() { }
        public void OpenSettings(string? moduleId = null) { }
    }
}

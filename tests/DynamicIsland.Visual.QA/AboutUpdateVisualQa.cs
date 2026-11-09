using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.App.Services;
using DynamicIsland.App.Island;
using DynamicIsland.Core.Settings;
using DynamicIsland.App.Settings;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Visual.QA;

/// <summary>Real settings Window/template with a local fake updater: no network, installation or user settings.</summary>
internal static class AboutUpdateVisualQa
{
    private static readonly string[] Languages = ["ru", "en"];
    private static readonly int[] Widths = [640, 780];
    private static readonly int[] Dpis = [96, 144];
    private static readonly (string Name, UpdateState State, int? Percent, UpdateFailure? Failure)[] States =
    [
        ("idle", UpdateState.Idle, null, null),
        ("checking", UpdateState.Checking, null, null),
        ("latest", UpdateState.Latest, null, null),
        ("available", UpdateState.Available, null, null),
        ("downloading-unknown", UpdateState.Downloading, null, null),
        ("downloading-37", UpdateState.Downloading, 37, null),
        ("ready", UpdateState.Ready, 100, null),
        ("network-failure", UpdateState.Error, null, new(UpdateStage.Checking, UpdateFailureReason.Network)),
        ("invalid-feed", UpdateState.Error, null, new(UpdateStage.Checking, UpdateFailureReason.InvalidFeed)),
        ("integrity-failure", UpdateState.Error, null, new(UpdateStage.Downloading, UpdateFailureReason.Integrity)),
        ("portable", UpdateState.Unsupported, null, null),
        ("cancelled", UpdateState.Cancelled, null, null),
    ];

    internal static async Task CaptureAsync(string output)
    {
        Directory.CreateDirectory(output);
        IslandTheme.Apply(IslandBackground.Black);
        using var about = new AboutViewModel();
        var updater = new VisualUpdater(about.Version);
        about.AttachUpdates(updater, new QaDispatcher());
        about.ExitForUpdate = () => Task.CompletedTask;
        var fixture = new SettingsFixture(new SettingsSection("about", "Settings.About", "Info", about));
        // The constructor's data is replaced before showing. No GeneralSettingsViewModel
        // is created: this cannot write registry/autostart or persistent user settings.
        var window = new SettingsWindow(null!) { DataContext = fixture };
        var evidence = new List<object>();
        try
        {
            window.Show();
            await PumpAsync();
            foreach (var language in Languages)
            {
                Loc.Instance.SetLanguage(language);
                window.DataContext = new SettingsFixture(new SettingsSection("about", "Settings.About", "Info", about));
                foreach (var state in States)
                {
                    updater.Publish(state.State, state.Percent, state.Failure);
                    foreach (var width in Widths)
                    {
                        window.Width = width;
                        await PumpAsync();
                        window.UpdateLayout();
                        var label = Descendants(window).OfType<TextBlock>().Single(block => block.Name == "UpdateStatusLabel");
                        if (label.Text != about.UpdateStatus || string.IsNullOrWhiteSpace(label.Text))
                            throw new InvalidOperationException("Visible update status did not match the real About view model");
                        if (about.IsUpdateBusy && about.CheckForUpdatesCommand.CanExecute(null))
                            throw new InvalidOperationException("Update check remained enabled during a busy operation");
                        foreach (var button in Descendants(window).OfType<Button>().Where(button => button.IsVisible))
                        {
                            var origin = button.TranslatePoint(new Point(), window);
                            if (origin.X < -1 || origin.X + button.ActualWidth > window.ActualWidth + 1)
                                throw new InvalidOperationException("Settings update action overflows the real window");
                        }
                        foreach (var dpi in Dpis)
                        {
                            var frame = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi / 96d),
                                (int)Math.Ceiling(window.ActualHeight * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
                            frame.Render(window);
                            var name = $"updates-{language}-{state.Name}-{width}wide-{dpi}bitmapdpi.png";
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(frame));
                            using (var stream = File.Create(Path.Combine(output, name))) encoder.Save(stream);
                            evidence.Add(new { File = name, Language = language, State = state.Name, Width = width, BitmapDpi = dpi,
                                ActualWindowDpi = VisualTreeHelper.GetDpi(window).PixelsPerInchX, Text = label.Text,
                                about.IsUpdateBusy, about.IsUpdateProgressIndeterminate, about.UpdateProgress,
                                about.ShowDownloadUpdate, about.ShowApplyUpdate, about.ShowReleaseLink });
                        }
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "about-update-qa.json"), JsonSerializer.Serialize(new
            {
                Description = "Actual SettingsWindow and About template with a fake local updater; bitmap DPI, not physical monitor or real network/update acceptance",
                Frames = evidence,
            }));
            Console.WriteLine($"PASS: {evidence.Count} real settings-window update-state renders");
        }
        finally { window.CloseForExit(); }
    }

    private static async Task PumpAsync()
    {
        await Task.Delay(60);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private sealed class SettingsFixture(SettingsSection selected)
    {
        public ObservableCollection<SettingsSection> Sections { get; } = [selected];
        public SettingsSection Selected { get; set; } = selected;
    }

    private sealed class VisualUpdater(string installedVersion) : IApplicationUpdateController
    {
        public UpdateSnapshot Current { get; private set; } = new(UpdateState.Idle, installedVersion, "9.9.9",
            AboutViewModel.RepositoryUrl + "/releases/latest", null, null, null);
        public event EventHandler? Changed;
        public bool IsInstalled => Current.State != UpdateState.Unsupported;
        public bool IsBusy => Current.State is UpdateState.Checking or UpdateState.Downloading;
        public void Publish(UpdateState state, int? percent, UpdateFailure? failure)
        {
            Current = Current with { State = state, ProgressPercent = percent, Failure = failure,
                CheckedAt = state == UpdateState.Checking ? null : new DateTimeOffset(2026, 11, 10, 12, 30, 0, TimeSpan.Zero) };
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public Task CheckAsync(bool notify = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Cancel() { }
    }
}

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core.Status;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.App.Island;

/// <summary>Battery and privacy indicators shown in the island chrome.</summary>
internal sealed partial class StatusViewModel : ObservableObject
{
    private readonly IBatteryService _battery;
    private readonly IPrivacyMonitor _privacy;

    [ObservableProperty]
    private BatteryStatus _batteryStatus = BatteryStatus.None;

    [ObservableProperty]
    private string _batteryText = string.Empty;

    [ObservableProperty]
    private bool _cameraInUse;

    [ObservableProperty]
    private bool _microphoneInUse;

    [ObservableProperty]
    private bool _recording;

    [ObservableProperty]
    private string _privacyText = string.Empty;

    /// <summary>Tab title of the browser in the foreground, or null.</summary>
    [ObservableProperty]
    private string? _browserTitle;

    private readonly DynamicIsland.Platform.Windows.Status.ForegroundBrowserWatcher _browser;

    public StatusViewModel(IBatteryService battery, IPrivacyMonitor privacy, DynamicIsland.Platform.Windows.Status.ForegroundBrowserWatcher browser)
    {
        _browser = browser;
        browser.Changed += (_, _) => BrowserTitle = browser.TabTitle;
        _battery = battery;
        _privacy = privacy;
        battery.Changed += (_, _) => UpdateBattery();
        privacy.Changed += (_, _) => UpdatePrivacy();
        Loc.Instance.CultureChanged += (_, _) => UpdatePrivacy();
    }

    /// <summary>Starts both services and reads their initial state.</summary>
    public void Start()
    {
        _battery.Start();
        _privacy.Start();
        _browser.Start();
        BrowserTitle = _browser.TabTitle;
        UpdateBattery();
        UpdatePrivacy();
    }

    private void UpdateBattery()
    {
        BatteryStatus = _battery.Current;
        BatteryText = _battery.Current.IsPresent ? _battery.Current.Percent.ToString(CultureInfo.InvariantCulture) + "%" : string.Empty;
    }

    private void UpdatePrivacy()
    {
        var status = _privacy.Current;
        CameraInUse = status.CameraInUse;
        MicrophoneInUse = status.MicrophoneInUse;
        Recording = status.Recording;
        PrivacyText = Describe(status, Loc.Instance);
    }

    /// <summary>Short line such as "Camera: Zoom · Microphone: Zoom, Discord".</summary>
    internal static string Describe(PrivacyStatus status, Loc loc)
    {
        var parts = new List<string>(3);
        string Names(IEnumerable<string> apps) =>
            string.Join(", ", apps.Select(a => a == AppNames.SystemApp ? loc["Privacy.SystemApp"] : a).Distinct());

        if (status.CameraInUse)
        {
            parts.Add($"{loc["Privacy.Camera"]}: {Names(status.CameraApps)}");
        }

        if (status.MicrophoneInUse)
        {
            parts.Add($"{loc["Privacy.Microphone"]}: {Names(status.MicrophoneApps)}");
        }

        if (status.Recording)
        {
            parts.Add($"{loc["Privacy.Recording"]}: {Names(status.RecordingApps)}");
        }

        return string.Join(" · ", parts);
    }
}

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

    public StatusViewModel(IBatteryService battery, IPrivacyMonitor privacy)
    {
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
        if (status.CameraInUse)
        {
            parts.Add($"{loc["Privacy.Camera"]}: {string.Join(", ", status.CameraApps)}");
        }

        if (status.MicrophoneInUse)
        {
            parts.Add($"{loc["Privacy.Microphone"]}: {string.Join(", ", status.MicrophoneApps)}");
        }

        if (status.Recording)
        {
            parts.Add($"{loc["Privacy.Recording"]}: {string.Join(", ", status.RecordingApps)}");
        }

        return string.Join(" · ", parts);
    }
}

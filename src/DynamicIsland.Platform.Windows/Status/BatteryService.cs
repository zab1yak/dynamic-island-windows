using DynamicIsland.Core.Status;
using DynamicIsland.Core.Threading;
using Microsoft.Extensions.Logging;
using Windows.Devices.Power;
using Windows.System.Power;
using CoreBatteryStatus = DynamicIsland.Core.Status.BatteryStatus;
using WinRtBatteryStatus = Windows.System.Power.BatteryStatus;

namespace DynamicIsland.Platform.Windows.Status;

/// <summary>
/// Battery level, charging and energy saver from WinRT events (<c>Battery.AggregateBattery</c>,
/// <c>PowerManager</c>); no polling.
/// </summary>
public sealed class BatteryService(IUiDispatcher dispatcher, ILogger<BatteryService> logger) : IBatteryService, IDisposable
{
    private Battery? _battery;
    private bool _started;

    /// <inheritdoc />
    public CoreBatteryStatus Current { get; private set; } = CoreBatteryStatus.None;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            _battery = Battery.AggregateBattery;
            _battery.ReportUpdated += OnSystemChange;
            PowerManager.EnergySaverStatusChanged += OnSystemChange;
            PowerManager.PowerSupplyStatusChanged += OnSystemChange;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Battery information is unavailable");
        }

        Refresh();
    }

    /// <summary>Reads the current state (also after resume from sleep).</summary>
    public void Refresh()
    {
        var status = Read();
        if (status == Current)
        {
            return;
        }

        Current = status;
        if (dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            dispatcher.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_battery is not null)
        {
            _battery.ReportUpdated -= OnSystemChange;
            PowerManager.EnergySaverStatusChanged -= OnSystemChange;
            PowerManager.PowerSupplyStatusChanged -= OnSystemChange;
            _battery = null;
        }
    }

    private void OnSystemChange(object? sender, object e) => Refresh();

    private CoreBatteryStatus Read()
    {
        if (_battery is null)
        {
            return CoreBatteryStatus.None;
        }

        try
        {
            var report = _battery.GetReport();
            if (report.Status == WinRtBatteryStatus.NotPresent)
            {
                return CoreBatteryStatus.None;
            }

            var full = report.FullChargeCapacityInMilliwattHours ?? 0;
            var remaining = report.RemainingCapacityInMilliwattHours ?? 0;
            var percent = full > 0 ? (int)Math.Round(100.0 * remaining / full) : PowerManager.RemainingChargePercent;
            var pluggedIn = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
            var charging = report.Status == WinRtBatteryStatus.Charging || (pluggedIn && report.Status == WinRtBatteryStatus.Idle);
            var saver = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
            return new CoreBatteryStatus(true, Math.Clamp(percent, 0, 100), charging, saver);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Battery report failed");
            return Current;
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Status;
using DynamicIsland.Core.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DynamicIsland.Platform.Windows.Status;

/// <summary>
/// Camera and microphone usage from <c>HKCU\…\CapabilityAccessManager\ConsentStore</c>, watched with
/// <c>RegNotifyChangeKeyValue</c> (no polling), plus an optional screen-recording heuristic that
/// looks for known recorder processes every 15 seconds.
/// </summary>
public sealed partial class PrivacyMonitor : IPrivacyMonitor, IDisposable
{
    private const string ConsentStorePath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const uint REG_NOTIFY_CHANGE_NAME = 0x1;
    private const uint REG_NOTIFY_CHANGE_LAST_SET = 0x4;
    private const uint REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RecorderCheckInterval = TimeSpan.FromSeconds(15);

    /// <summary>Processes treated as screen recorders (heuristic; documented in the README).</summary>
    internal static readonly string[] RecorderProcesses = ["obs64", "obs32", "obs", "ShareX", "bdcam", "CamtasiaRecorder", "FlashbackRecorder"];

    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<PrivacyMonitor> _logger;
    private readonly List<Watch> _watches = [];
    private readonly Lock _gate = new();
    private Timer? _debounce;
    private Timer? _recorderTimer;
    private Timer? _activeTimer;
    private Timer? _sharingTimer;
    private IReadOnlyList<string> _sharing = [];
    private static readonly TimeSpan ActiveRecheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SharingCheckInterval = TimeSpan.FromSeconds(3);
    private bool _detectRecording;
    private IReadOnlyList<string> _recorders = [];
    private volatile bool _disposed;

    // Timer callbacks may overlap (1 s recheck, 3 s sharing check, registry events): one scan at a time.
    private readonly Lock _scanLock = new();

    public PrivacyMonitor(IUiDispatcher dispatcher, ILogger<PrivacyMonitor> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public PrivacyStatus Current { get; private set; } = PrivacyStatus.Idle;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Start()
    {
        if (_watches.Count > 0)
        {
            return;
        }

        foreach (var device in new[] { "webcam", "microphone" })
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey($@"{ConsentStorePath}\{device}", writable: false);
                if (key is null)
                {
                    continue;
                }

                var watch = new Watch(key, new AutoResetEvent(false));
                watch.Registration = ThreadPool.RegisterWaitForSingleObject(watch.Signal, (_, _) => OnRegistryChanged(watch), null, Timeout.Infinite, executeOnlyOnce: false);
                Arm(watch);
                _watches.Add(watch);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _logger.LogWarning(ex, "Cannot watch {Device} usage", device);
            }
        }

        SafeScan();
        // Sharing windows (browser "sharing your screen" bar, Zoom/Teams toolbars) and Game Bar capture.
        _sharingTimer = new Timer(_ => SafeCheckSharing(), null, TimeSpan.FromSeconds(2), SharingCheckInterval);
    }

    /// <summary>Turns the screen-recording heuristic on or off.</summary>
    public void SetRecordingDetection(bool enabled)
    {
        _detectRecording = enabled;
        _recorderTimer?.Dispose();
        _recorderTimer = null;
        if (enabled)
        {
            _recorderTimer = new Timer(_ => SafeCheckRecorders(), null, TimeSpan.Zero, RecorderCheckInterval);
        }
        else
        {
            _recorders = [];
            SafeScan();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _recorderTimer?.Dispose();
        _activeTimer?.Dispose();
        _sharingTimer?.Dispose();
        _debounce?.Dispose();
        foreach (var watch in _watches)
        {
            // Wait for an in-flight callback before the key and the event are closed.
            using (var done = new ManualResetEvent(false))
            {
                if (watch.Registration?.Unregister(done) == true)
                {
                    done.WaitOne(TimeSpan.FromSeconds(1));
                }
            }

            watch.Signal.Dispose();
            watch.Key.Dispose();
        }

        _watches.Clear();
    }

    private void OnRegistryChanged(Watch watch)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // A notification fires once; re-arm before scanning so no change is missed.
            Arm(watch);
            lock (_gate)
            {
                _debounce?.Dispose();
                _debounce = new Timer(_ => SafeScan(), null, Debounce, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Registry watch re-arm failed");
        }
    }

    /// <summary>Thread-pool callbacks must never throw: an unhandled exception there ends the process.</summary>
    private void SafeScan()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            lock (_scanLock)
            {
                if (!_disposed)
                {
                    Scan();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Privacy scan failed");
        }
    }

    private void Arm(Watch watch)
    {
        var result = RegNotifyChangeKeyValue(watch.Key.Handle, true,
            REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_THREAD_AGNOSTIC,
            watch.Signal.SafeWaitHandle, true);
        if (result != 0)
        {
            _logger.LogWarning("RegNotifyChangeKeyValue failed with {Error}", result);
        }
    }

    private void SafeCheckRecorders()
    {
        try
        {
            CheckRecorders();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recorder check failed");
        }
    }

    private void CheckRecorders()
    {
        if (!_detectRecording)
        {
            return;
        }

        var found = new List<string>();
        foreach (var name in RecorderProcesses)
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length > 0)
            {
                found.Add(name);
            }

            foreach (var p in processes)
            {
                p.Dispose();
            }
        }

        _recorders = found;
        SafeScan();
    }

    private void Scan()
    {
        PrivacyStatus status;
        try
        {
            var consentMicrophone = ConsentStoreParser.AppsInUse(ReadEntries("microphone"), IsProcessRunning, NameOf);
            status = new PrivacyStatus(
                ConsentStoreParser.AppsInUse(ReadEntries("webcam"), IsProcessRunning, NameOf),
                // An open capture stream is the truth for the microphone; it ends the moment the app closes it.
                MicrophoneSignal.Merge(LiveCaptureSignals.MicrophoneSessions(NameOfProcess), consentMicrophone),
                [.. _sharing.Concat(_recorders).Distinct()]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Privacy scan failed");
            return;
        }

        lock (_gate)
        {
            UpdateActiveTimer(status.CameraInUse || status.MicrophoneInUse);
            if (status.SameAs(Current))
            {
                return;
            }

            Current = status;
        }

        _dispatcher.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// While a camera or microphone dot is lit, re-check every second so it goes out as soon as the
    /// device is released (ConsentStore may write the stop time late or never). Off when nothing is used.
    /// </summary>
    private void UpdateActiveTimer(bool active)
    {
        active &= !_disposed;
        if (active && _activeTimer is null)
        {
            _activeTimer = new Timer(_ => SafeScan(), null, ActiveRecheckInterval, ActiveRecheckInterval);
        }
        else if (!active && _activeTimer is not null)
        {
            _activeTimer.Dispose();
            _activeTimer = null;
        }
    }

    private void SafeCheckSharing()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            IReadOnlyList<string> sharing;
            lock (_scanLock)
            {
                sharing = LiveCaptureSignals.ScreenSharing();
            }

            if (!sharing.SequenceEqual(_sharing))
            {
                _sharing = sharing;
                SafeScan();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Screen sharing check failed");
        }
    }

    /// <summary>Readable name of the process owning a capture session.</summary>
    private string NameOfProcess(uint pid)
    {
        if (pid == 0)
        {
            return AppNames.SystemApp;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Elevated or protected process: fall back to the name.
            }

            var entry = new ConsentEntry((path ?? process.ProcessName + ".exe").Replace('\\', '#'), IsNonPackaged: true, 1, 0);
            // With a path the product name is read (cached); without one the known-apps list or the process name is used.
            return path is null ? AppNames.Resolve(entry) : NameOf(entry);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AppNames.SystemApp;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Readable app name (cached): exe version info, package display name, known apps.</summary>
    private string NameOf(ConsentEntry entry) =>
        _names.GetOrAdd((entry.IsNonPackaged ? "n:" : "p:") + entry.KeyName, _ => AppNames.Resolve(entry, ProductName, PackageName));

    private static string? ProductName(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
            {
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(exePath);
            return string.IsNullOrWhiteSpace(info.ProductName) ? info.FileDescription : info.ProductName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? PackageName(string familyName)
    {
        try
        {
            var manager = new global::Windows.Management.Deployment.PackageManager();
            var package = manager.FindPackagesForUser(string.Empty, familyName).FirstOrDefault();
            var name = package?.DisplayName;
            // Unresolved resource strings look like "ms-resource:..."; those are useless.
            return name is null || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? null : name;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether a process started from <paramref name="exePath"/> is alive (by name; path when readable).</summary>
    internal static bool IsProcessRunning(string exePath)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath));
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null || path.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Elevated or exiting process: the name match is the best we can do.
                    return true;
                }
            }

            return false;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static List<ConsentEntry> ReadEntries(string device)
    {
        var entries = new List<ConsentEntry>();
        using var root = Registry.CurrentUser.OpenSubKey($@"{ConsentStorePath}\{device}", writable: false);
        if (root is null)
        {
            return entries;
        }

        Collect(root, isNonPackaged: false, entries);
        using var nonPackaged = root.OpenSubKey("NonPackaged", writable: false);
        if (nonPackaged is not null)
        {
            Collect(nonPackaged, isNonPackaged: true, entries);
        }

        return entries;
    }

    private static void Collect(RegistryKey parent, bool isNonPackaged, List<ConsentEntry> entries)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (!isNonPackaged && name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var app = parent.OpenSubKey(name, writable: false);
            if (app is null)
            {
                continue;
            }

            entries.Add(new ConsentEntry(name, isNonPackaged, ReadFileTime(app, "LastUsedTimeStart"), ReadFileTime(app, "LastUsedTimeStop")));
        }
    }

    private static long ReadFileTime(RegistryKey key, string name) => key.GetValue(name) switch
    {
        long l => l,
        int i => i,
        _ => 0,
    };

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(SafeRegistryHandle hKey, [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter, SafeWaitHandle hEvent, [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    private sealed class Watch(RegistryKey key, AutoResetEvent signal)
    {
        public RegistryKey Key { get; } = key;

        public AutoResetEvent Signal { get; } = signal;

        public RegisteredWaitHandle? Registration { get; set; }
    }
}

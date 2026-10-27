using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Status;
using DynamicIsland.Platform.Windows.Native;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Platform.Windows.Status;

/// <summary>
/// Live signals Windows exposes without administrator rights: active microphone capture sessions
/// (Core Audio) and windows that apps show while they share the screen.
/// </summary>
internal static partial class LiveCaptureSignals
{
    /// <summary>
    /// Apps with an active capture session on any microphone, or <c>null</c> when Core Audio cannot
    /// be read (then ConsentStore is used instead).
    /// </summary>
    public static IReadOnlyList<string>? MicrophoneSessions(Func<uint, string> nameOfProcess)
    {
        try
        {
            var apps = new List<string>();
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                        {
                            apps.Add(nameOfProcess(session.GetProcessID));
                        }
                    }
                }
            }

            return apps;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>Apps that show a "sharing your screen" window or run a capture-only process.</summary>
    public static IReadOnlyList<string> ScreenSharing()
    {
        var found = new List<string>();
        var names = ProcessNames;
        if (Environment.TickCount64 - s_namesStamp > 60_000)
        {
            // Process ids are reused: forget cached names once a minute.
            names.Clear();
            s_namesStamp = Environment.TickCount64;
        }

        EnumWindows((hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (!names.TryGetValue(pid, out var process))
            {
                process = ProcessName(pid);
                names[pid] = process;
            }

            // Only a few apps show sharing windows; skip reading titles of everything else.
            if (process is null || !ScreenShareSignatures.IsCandidate(process))
            {
                return true;
            }

            if (ScreenShareSignatures.Match(process, NativeMethods.ClassNameOf(hwnd), Title(hwnd)) is { } app)
            {
                found.Add(app);
            }

            return true;
        }, 0);

        // Enumerating all processes is the expensive part: Game Bar capture is checked every 15 s only.
        if (Environment.TickCount64 - s_captureStamp >= 15_000)
        {
            s_captureStamp = Environment.TickCount64;
            s_gameBarCapturing = ScreenShareSignatures.CaptureOnlyProcesses.Any(IsRunning);
        }

        if (s_gameBarCapturing)
        {
            found.Add("Xbox Game Bar");
        }

        return found.Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static long s_captureStamp = long.MinValue / 2;
    private static bool s_gameBarCapturing;

    private static bool IsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var p in processes)
        {
            p.Dispose();
        }

        return processes.Length > 0;
    }

    private static readonly Dictionary<uint, string?> ProcessNames = [];
    private static long s_namesStamp;

    private static string? ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static unsafe string Title(nint hwnd)
    {
        var buffer = stackalloc char[512];
        var length = NativeMethods.GetWindowText(hwnd, buffer, 512);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

#pragma warning disable SYSLIB1054 // A managed callback delegate is simplest with the classic marshaller here.
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

#pragma warning restore SYSLIB1054
}

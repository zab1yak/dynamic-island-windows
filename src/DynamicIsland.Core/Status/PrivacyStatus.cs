namespace DynamicIsland.Core.Status;

/// <summary>A device the privacy indicators track.</summary>
public enum PrivacyDevice
{
    /// <summary>Webcam.</summary>
    Camera,

    /// <summary>Microphone.</summary>
    Microphone,

    /// <summary>Screen recording (heuristic).</summary>
    ScreenRecording,
}

/// <summary>Which apps currently use the camera, microphone or record the screen.</summary>
public sealed record PrivacyStatus(IReadOnlyList<string> CameraApps, IReadOnlyList<string> MicrophoneApps, IReadOnlyList<string> RecordingApps)
{
    /// <summary>Nothing in use.</summary>
    public static PrivacyStatus Idle { get; } = new([], [], []);

    /// <summary>Camera is in use.</summary>
    public bool CameraInUse => CameraApps.Count > 0;

    /// <summary>Microphone is in use.</summary>
    public bool MicrophoneInUse => MicrophoneApps.Count > 0;

    /// <summary>The screen seems to be recorded.</summary>
    public bool Recording => RecordingApps.Count > 0;

    /// <summary>Anything in use.</summary>
    public bool Any => CameraInUse || MicrophoneInUse || Recording;

    /// <summary>Value equality including list contents.</summary>
    public bool SameAs(PrivacyStatus other) =>
        CameraApps.SequenceEqual(other.CameraApps) && MicrophoneApps.SequenceEqual(other.MicrophoneApps) && RecordingApps.SequenceEqual(other.RecordingApps);
}

/// <summary>Watches camera / microphone usage and screen recording.</summary>
public interface IPrivacyMonitor
{
    /// <summary>Latest status.</summary>
    PrivacyStatus Current { get; }

    /// <summary>Raised on the UI thread when the status changes.</summary>
    event EventHandler? Changed;

    /// <summary>Starts watching.</summary>
    void Start();
}

/// <summary>One app entry under <c>CapabilityAccessManager\ConsentStore\{device}</c>.</summary>
/// <param name="KeyName">Registry key name: a package family name, or a path with <c>#</c> instead of <c>\</c> under <c>NonPackaged</c>.</param>
/// <param name="IsNonPackaged">Whether the key lives under <c>NonPackaged</c>.</param>
/// <param name="LastUsedTimeStart">FILETIME of the last start, 0 if never.</param>
/// <param name="LastUsedTimeStop">FILETIME of the last stop; 0 while the app is using the device.</param>
public readonly record struct ConsentEntry(string KeyName, bool IsNonPackaged, long LastUsedTimeStart, long LastUsedTimeStop);

/// <summary>
/// Interprets the (undocumented) ConsentStore layout defensively: an app uses the device when it
/// has started (<c>LastUsedTimeStart != 0</c>) and not stopped (<c>LastUsedTimeStop == 0</c>).
/// </summary>
public static class ConsentStoreParser
{
    /// <summary>Display names of apps that currently use the device, sorted and without duplicates.</summary>
    /// <param name="entries">Registry entries.</param>
    /// <param name="isRunning">
    /// Checks a desktop app by its executable path. An app that crashed never writes
    /// <c>LastUsedTimeStop</c>, so a non-packaged entry only counts while its process is alive.
    /// </param>
    /// <param name="name">Readable name of an entry; defaults to <see cref="DisplayName"/>.</param>
    public static IReadOnlyList<string> AppsInUse(IEnumerable<ConsentEntry> entries, Func<string, bool>? isRunning = null, Func<ConsentEntry, string>? name = null) =>
        entries
            .Where(e => e.LastUsedTimeStart != 0 && e.LastUsedTimeStop == 0)
            .Where(e => !e.IsNonPackaged || isRunning is null || isRunning(ExecutablePath(e)))
            .Select(name ?? DisplayName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Executable path of a non-packaged entry (<c>#</c> back to <c>\</c>).</summary>
    public static string ExecutablePath(ConsentEntry entry) => entry.KeyName.Replace('#', '\\');

    /// <summary>
    /// Readable name: <c>C:#Program Files#Zoom#bin#Zoom.exe</c> → <c>Zoom</c>,
    /// <c>Microsoft.WindowsCamera_8wekyb3d8bbwe</c> → <c>WindowsCamera</c>.
    /// </summary>
    public static string DisplayName(ConsentEntry entry)
    {
        var name = entry.KeyName;
        if (entry.IsNonPackaged)
        {
            var file = name[(name.LastIndexOf('#') + 1)..];
            return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        }

        var underscore = name.IndexOf('_', StringComparison.Ordinal);
        var family = underscore > 0 ? name[..underscore] : name;
        var dot = family.LastIndexOf('.');
        return dot >= 0 && dot < family.Length - 1 ? family[(dot + 1)..] : family;
    }
}

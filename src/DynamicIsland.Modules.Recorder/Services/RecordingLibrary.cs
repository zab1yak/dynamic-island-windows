using System.Globalization;

namespace DynamicIsland.Modules.Recorder.Services;

/// <summary>Audio format of saved recordings.</summary>
public enum RecordingFormat
{
    /// <summary>Uncompressed WAV (always available).</summary>
    Wav,

    /// <summary>MP3 via Windows Media Foundation (missing on Windows "N" editions without the Media Feature Pack).</summary>
    Mp3,
}

/// <summary>Recorder settings (<c>recorder.json</c>).</summary>
public sealed class RecorderSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Folder for recordings; <c>null</c> = Music\DynamicIsland.</summary>
    public string? Folder { get; set; }

    public RecordingFormat Format { get; set; } = RecordingFormat.Mp3;

    /// <summary>Default folder: the user's Music folder (may be redirected to OneDrive).</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "DynamicIsland");

    /// <summary>The folder actually used: the configured one when it is acceptable, otherwise the default.</summary>
    public string EffectiveFolder => IsAcceptableFolder(Folder) ? Folder! : DefaultFolder;

    /// <summary>
    /// A local, fully qualified folder that is not a drive root, not inside Windows or Program Files and not
    /// a junction or symbolic link. Network paths are refused: opening them could hang the island and send
    /// Windows credentials to the server.
    /// </summary>
    public static bool IsAcceptableFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (Path.GetPathRoot(full) is not { } root || string.Equals(Path.TrimEndingDirectorySeparator(root), full, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] system =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];
        if (system.Any(s => s.Length > 0 && (string.Equals(full, s, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory)
            {
                return false;
            }

            var info = new DirectoryInfo(full);
            return !info.Exists || (info.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A saved recording.</summary>
public sealed record RecordingFile(string Path, string Name, DateTime CreatedAt, long SizeBytes);

/// <summary>Names, lists and deletes recordings in the recordings folder (only files this module creates).</summary>
public static class RecordingLibrary
{
    /// <summary>Prefix of every file the recorder writes.</summary>
    public const string Prefix = "Recording ";

    /// <summary>Most recordings listed on the page.</summary>
    public const int ListLimit = 20;

    /// <summary>File name for a recording started at <paramref name="local"/>.</summary>
    public static string FileName(DateTime local, RecordingFormat format) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{local:yyyy-MM-dd HH-mm-ss}.{(format == RecordingFormat.Mp3 ? "mp3" : "wav")}");

    /// <summary>Path that does not overwrite an existing file (adds " (2)" etc.).</summary>
    public static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){extension}");
        }

        return path;
    }

    /// <summary>The latest recordings in <paramref name="folder"/>, newest first.</summary>
    public static IReadOnlyList<RecordingFile> List(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return new DirectoryInfo(folder)
            .EnumerateFiles(Prefix + "*")
            .Where(f => f.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Take(ListLimit)
            .Select(f => new RecordingFile(f.FullName, Path.GetFileNameWithoutExtension(f.Name)[Prefix.Length..], f.CreationTime, f.Length))
            .ToList();
    }

    /// <summary>
    /// Deletes a recording, but only a <c>.wav</c>/<c>.mp3</c> file the recorder created directly inside
    /// <paramref name="folder"/> (not in a subfolder, not a link).
    /// </summary>
    public static bool Delete(string folder, string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var name = Path.GetFileName(full);
            var extension = Path.GetExtension(full);
            if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase)
                || !name.StartsWith(Prefix, StringComparison.Ordinal)
                || !(extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var file = new FileInfo(full);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary><c>m:ss</c> or <c>h:mm:ss</c>.</summary>
    public static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}");
}

/// <summary>Rolling history of microphone levels drawn as iPhone-style waveform bars.</summary>
public sealed class WaveformHistory(int bars = 28)
{
    private readonly double[] _levels = Enumerable.Repeat(0.06, bars).ToArray();

    /// <summary>Bar heights 0–1, oldest first.</summary>
    public IReadOnlyList<double> Levels => _levels;

    /// <summary>Appends a peak sample (0–1); quiet input still shows a thin line.</summary>
    public void Push(double peak)
    {
        Array.Copy(_levels, 1, _levels, 0, _levels.Length - 1);
        _levels[^1] = Math.Clamp(0.06 + (Math.Sqrt(Math.Clamp(peak, 0, 1)) * 0.94), 0.06, 1);
    }

    /// <summary>Flat line.</summary>
    public void Reset() => Array.Fill(_levels, 0.06);
}

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

    /// <summary>The folder actually used (falls back to the default for empty or relative values).</summary>
    public string EffectiveFolder => !string.IsNullOrWhiteSpace(Folder) && Path.IsPathFullyQualified(Folder) ? Folder : DefaultFolder;
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

    /// <summary>Deletes a recording, but only a file the recorder created inside <paramref name="folder"/>.</summary>
    public static bool Delete(string folder, string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            File.Delete(full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

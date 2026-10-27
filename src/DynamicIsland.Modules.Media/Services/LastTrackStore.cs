using System.Security.Cryptography;
using DynamicIsland.Core.Storage;

namespace DynamicIsland.Modules.Media.Services;

/// <summary>The last track, as persisted to <c>media-lasttrack.json</c>.</summary>
public sealed class SavedTrack
{
    public int SchemaVersion { get; set; } = 1;

    public string AppId { get; set; } = string.Empty;

    public string AppName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    /// <summary>Artwork file name inside the <c>art</c> cache folder, or <c>null</c>.</summary>
    public string? ArtworkFile { get; set; }

    public double PositionSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public bool CanSeek { get; set; }

    public DateTimeOffset SavedAt { get; set; }
}

/// <summary>
/// Persists the playing track so that after a reboot the island can show it paused at the same
/// position. Saves on track change, on pause/stop, every <see cref="PeriodicInterval"/> while
/// playing, and when Windows ends the session; never more often than needed.
/// <para>
/// After a restart the saved position is protected: players that come back with the same track
/// report 0:00 (or a few seconds from a stale timestamp) and a closing player reports Stopped at 0.
/// Neither may overwrite the saved position. The protection ends when the same track reaches the
/// saved position (or the user seeks past it), plays for <see cref="RestoreGuardPlayTime"/>, or a
/// different track appears.
/// </para>
/// </summary>
public sealed class LastTrackStore(IStorageService storage, TimeProvider time) : IDisposable
{
    /// <summary>File name relative to the data folder.</summary>
    public const string FileName = "media-lasttrack.json";

    /// <summary>Artwork cache folder relative to the data folder.</summary>
    public const string ArtFolder = "art";

    /// <summary>Periodic save interval while playing.</summary>
    public static readonly TimeSpan PeriodicInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long a reopened track must play before a smaller position may replace the saved one.</summary>
    public static readonly TimeSpan RestoreGuardPlayTime = TimeSpan.FromSeconds(10);

    private const int KeepArtworkFiles = 8;
    private const long MaxArtworkBytes = 4 * 1024 * 1024;
    private bool _saving;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SavedTrack? _last;
    private PlaybackState _lastState;
    private DateTimeOffset _lastSaved = DateTimeOffset.MinValue;
    private bool _guardArmed;
    private DateTimeOffset? _guardPlayingSince;

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>Decides whether <paramref name="snapshot"/> should be saved now, and saves it.</summary>
    /// <returns><c>true</c> when a save happened.</returns>
    public async Task<bool> OnSnapshotAsync(MediaSnapshot? snapshot, bool force = false)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Title))
        {
            return false;
        }

        if (_saving && !force)
        {
            return false;
        }

        // Stopped means closed or unknown: its position is meaningless (players report 0 when they exit).
        if (snapshot.State == PlaybackState.Stopped)
        {
            return false;
        }

        var now = time.GetUtcNow();
        if (IsGuarded(snapshot, now))
        {
            return false;
        }

        var trackChanged = _last is null || _last.AppId != snapshot.AppId || _last.Title != snapshot.Title || _last.Artist != snapshot.Artist;
        var stateChanged = snapshot.State != _lastState;
        var periodic = snapshot.IsPlaying && now - _lastSaved >= PeriodicInterval;
        if (!force && !trackChanged && !stateChanged && !periodic)
        {
            return false;
        }

        await SaveAsync(snapshot, now, trackChanged || _last?.ArtworkFile is null);
        return true;
    }

    /// <summary>Loads the saved track and its artwork bytes, or <c>null</c>.</summary>
    public async Task<(SavedTrack Track, byte[]? Artwork)?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var track = await storage.LoadAsync<SavedTrack>(FileName, cancellationToken);
        if (track is null || string.IsNullOrWhiteSpace(track.AppId) || string.IsNullOrWhiteSpace(track.Title))
        {
            return null;
        }

        byte[]? artwork = null;
        if (IsSafeArtworkName(track.ArtworkFile))
        {
            var file = new FileInfo(storage.ResolvePath(Path.Combine(ArtFolder, track.ArtworkFile!)));
            if (file.Exists && file.Length <= MaxArtworkBytes && (file.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                artwork = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
            }
        }

        _last = track;
        _lastState = PlaybackState.Paused;
        _lastSaved = time.GetUtcNow();
        _guardArmed = true;
        _guardPlayingSince = null;
        return (track, artwork);
    }

    /// <summary>Converts a saved track into a paused snapshot at the saved position.</summary>
    public static MediaSnapshot ToSnapshot(SavedTrack track, byte[]? artwork) => new(
        track.AppId, track.AppName, track.Title, track.Artist, track.Album, artwork, PlaybackState.Paused,
        TimeSpan.FromSeconds(Math.Max(0, track.PositionSeconds)), TimeSpan.FromSeconds(Math.Max(0, track.DurationSeconds)),
        track.SavedAt, 1.0, track.CanSeek, false, false);

    /// <summary>Artwork names are always <c>&lt;sha256&gt;.img</c>; anything else is ignored.</summary>
    internal static bool IsSafeArtworkName(string? name) =>
        name is { Length: 68 } && name.EndsWith(".img", StringComparison.Ordinal) && name[..64].All(char.IsAsciiHexDigitLower);

    private bool IsGuarded(MediaSnapshot snapshot, DateTimeOffset now)
    {
        if (!_guardArmed || _last is null)
        {
            return false;
        }

        var sameTrack = _last.AppId == snapshot.AppId && _last.Title == snapshot.Title && _last.Artist == snapshot.Artist;
        var position = PositionInterpolator.At(snapshot, now).TotalSeconds;
        if (!sameTrack || position >= _last.PositionSeconds - 1)
        {
            _guardArmed = false;
            return false;
        }

        if (!snapshot.IsPlaying)
        {
            _guardPlayingSince = null;
            return true;
        }

        _guardPlayingSince ??= now;
        if (now - _guardPlayingSince.Value >= RestoreGuardPlayTime)
        {
            _guardArmed = false;
            return false;
        }

        return true;
    }

    private async Task SaveAsync(MediaSnapshot snapshot, DateTimeOffset now, bool writeArtwork)
    {
        await _gate.WaitAsync();
        _saving = true;
        try
        {
            var artworkFile = _last?.ArtworkFile;
            if (writeArtwork)
            {
                artworkFile = snapshot.Artwork is { Length: > 0 } art ? await WriteArtworkAsync(art) : null;
            }

            var track = new SavedTrack
            {
                AppId = snapshot.AppId,
                AppName = snapshot.AppName,
                Title = snapshot.Title,
                Artist = snapshot.Artist,
                Album = snapshot.Album,
                ArtworkFile = artworkFile,
                PositionSeconds = PositionInterpolator.At(snapshot, now).TotalSeconds,
                DurationSeconds = snapshot.Duration.TotalSeconds,
                CanSeek = snapshot.CanSeek,
                SavedAt = now,
            };
            await storage.SaveAsync(FileName, track);
            _last = track;
            _lastState = snapshot.State;
            _lastSaved = now;
        }
        finally
        {
            _saving = false;
            _gate.Release();
        }
    }

    private async Task<string> WriteArtworkAsync(byte[] artwork)
    {
        var name = Convert.ToHexStringLower(SHA256.HashData(artwork)) + ".img";
        var folder = storage.ResolvePath(ArtFolder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, artwork);
            File.Move(temp, path, overwrite: true);
            PruneArtwork(folder, keep: name);
        }

        return name;
    }

    private static void PruneArtwork(string folder, string keep)
    {
        var stale = new DirectoryInfo(folder).GetFiles("*.img")
            .Where(f => f.Name != keep)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(KeepArtworkFiles - 1);
        foreach (var file in stale)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // In use or already gone; try again next time.
            }
        }
    }
}

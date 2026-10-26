using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Core.Storage;

/// <summary>
/// <see cref="IStorageService"/> backed by JSON files. Each path has its own lock, so concurrent
/// saves of the same document are serialized and the last one wins.
/// </summary>
public sealed class JsonStorageService : IStorageService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<JsonStorageService> _logger;

    /// <summary>Creates the service rooted at <paramref name="rootPath"/>; the folder is created if missing.</summary>
    public JsonStorageService(string rootPath, ILogger<JsonStorageService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
        _logger = logger;
        Directory.CreateDirectory(RootPath);
    }

    /// <inheritdoc />
    public string RootPath { get; }

    /// <inheritdoc />
    public string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Path must be relative to the data folder.", nameof(relativePath));
        }

        var full = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        var rootWithSeparator = RootPath.EndsWith(Path.DirectorySeparatorChar) ? RootPath : RootPath + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Path escapes the data folder.", nameof(relativePath));
        }

        return full;
    }

    /// <inheritdoc />
    public async Task<T?> LoadAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class
    {
        var path = ResolvePath(relativePath);
        var gate = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                var corrupt = $"{path}.{DateTime.UtcNow:yyyyMMddHHmmss}.corrupt";
                _logger.LogError(ex, "Corrupt JSON in {Path}; moving it to {CorruptPath}", path, corrupt);
                try
                {
                    File.Move(path, corrupt, overwrite: true);
                }
                catch (IOException moveError)
                {
                    _logger.LogWarning(moveError, "Could not move the corrupt file aside");
                }

                return null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync<T>(string relativePath, T value, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var path = ResolvePath(relativePath);
        var gate = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Unique name: a restarting second process must never share our temp file.
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await WriteAndReplaceAsync(path, temp, value, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task WriteAndReplaceAsync<T>(string path, string temp, T value, CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            // File.Replace is atomic on NTFS and keeps the target's ACLs.
            File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}

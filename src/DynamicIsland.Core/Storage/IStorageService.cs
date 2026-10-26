namespace DynamicIsland.Core.Storage;

/// <summary>
/// Reads and writes JSON documents under the application data folder.
/// Writes are atomic: a crash or power loss never leaves a half-written file behind.
/// </summary>
public interface IStorageService
{
    /// <summary>Absolute path of the root data folder (for example <c>%LOCALAPPDATA%\DynamicIsland</c>).</summary>
    string RootPath { get; }

    /// <summary>
    /// Loads a document. Returns <c>null</c> when the file does not exist.
    /// A corrupt file is moved aside to <c>*.corrupt</c> and <c>null</c> is returned.
    /// </summary>
    /// <param name="relativePath">Path relative to <see cref="RootPath"/>, for example <c>todo.json</c>.</param>
    Task<T?> LoadAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class;

    /// <summary>Saves a document atomically (temporary file, flush to disk, then replace).</summary>
    /// <param name="relativePath">Path relative to <see cref="RootPath"/>.</param>
    Task SaveAsync<T>(string relativePath, T value, CancellationToken cancellationToken = default) where T : class;

    /// <summary>Resolves a relative path inside <see cref="RootPath"/>, rejecting anything that escapes it.</summary>
    /// <exception cref="ArgumentException">The path is rooted or escapes the data folder.</exception>
    string ResolvePath(string relativePath);
}

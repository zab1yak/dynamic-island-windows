using System.Globalization;
using System.Text.Json;
using DynamicIsland.Core.Storage;

namespace DynamicIsland.Modules.Shelf.Services;

/// <summary>How dropped files are kept.</summary>
public enum ShelfMode
{
    /// <summary>Copy into the shelf folder (the original may be moved or deleted).</summary>
    Copy,

    /// <summary>Remember the original path only (no disk space used).</summary>
    Link,
}

/// <summary>What an item is.</summary>
public enum ShelfItemKind
{
    File,
    Folder,
    Image,
    Text,
}

/// <summary>One shelf entry.</summary>
public sealed class ShelfItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public ShelfItemKind Kind { get; set; }

    /// <summary>Path relative to the shelf folder for copies (<c>items\&lt;id&gt;\name</c>).</summary>
    public string? StoredPath { get; set; }

    /// <summary>Absolute original path in link mode.</summary>
    public string? LinkedPath { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>Shelf settings and index (<c>index.json</c> inside the shelf folder).</summary>
public sealed class ShelfIndex
{
    public int SchemaVersion { get; set; } = 1;

    public ShelfMode Mode { get; set; } = ShelfMode.Copy;

    /// <summary>Items older than this are removed automatically (0 = never).</summary>
    public int AutoCleanupDays { get; set; } = 7;

    /// <summary>Total size limit for copies, in MB.</summary>
    public int MaxSizeMb { get; set; } = 2048;

    public List<ShelfItem> Items { get; set; } = [];

    public void Normalize()
    {
        AutoCleanupDays = Math.Clamp(AutoCleanupDays, 0, 365);
        MaxSizeMb = Math.Clamp(MaxSizeMb, 50, 100_000);
        Items ??= [];
        Items.RemoveAll(i => i is null);
        foreach (var item in Items)
        {
            item.SizeBytes = Math.Clamp(item.SizeBytes, 0, 1L << 50);
            item.Name = ShelfNames.Sanitize(item.Name);
        }
    }
}

/// <summary>Why an add was refused.</summary>
public enum ShelfAddError
{
    None,
    TooLarge,
    NotFound,
    Failed,
}

/// <summary>
/// The shelf on disk. Every copy lives in its own <c>items\&lt;guid&gt;</c> folder under a sanitized
/// name, so names never collide on disk and nothing can be written outside the shelf folder.
/// Folders are copied without following junctions or symbolic links.
/// </summary>
public sealed class ShelfStore
{
    private const string IndexFile = "index.json";
    private const string ItemsFolder = "items";

    private readonly TimeProvider _time;
    private ShelfIndex _index = new();

    public ShelfStore(string root, TimeProvider time)
    {
        Root = Path.GetFullPath(root);
        _time = time;
    }

    /// <summary>Absolute shelf folder.</summary>
    public string Root { get; }

    /// <summary>Settings and items.</summary>
    public ShelfIndex Index => _index;

    /// <summary>Items, newest first.</summary>
    public IReadOnlyList<ShelfItem> Items => _index.Items.OrderByDescending(i => i.AddedAt).ToList();

    /// <summary>Total bytes used by copies.</summary>
    public long UsedBytes => _index.Items.Where(i => i.StoredPath is not null).Sum(i => i.SizeBytes);

    /// <summary>Loads the index; items whose files vanished are dropped.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, IndexFile);
        if (File.Exists(path))
        {
            try
            {
                await using var stream = File.OpenRead(path);
                _index = await JsonSerializer.DeserializeAsync<ShelfIndex>(stream, JsonDefaults.Options, cancellationToken) ?? new ShelfIndex();
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                _index = new ShelfIndex();
            }
        }

        _index.Normalize();
        _index.Items.RemoveAll(i => !IsValid(i) || ResolvePath(i) is not { } p || !(File.Exists(p) || Directory.Exists(p)));
        SweepOrphans();
    }

    /// <summary>Deletes <c>items\&lt;guid&gt;</c> folders the index no longer knows about (failed or interrupted adds).</summary>
    private void SweepOrphans()
    {
        var items = Path.Combine(Root, ItemsFolder);
        if (!Directory.Exists(items))
        {
            return;
        }

        var known = _index.Items.Select(i => i.Id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new DirectoryInfo(items).EnumerateDirectories())
        {
            if (!known.Contains(folder.Name) && Guid.TryParseExact(folder.Name, "N", out _))
            {
                try
                {
                    folder.Delete(recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still locked; next start tries again.
                }
            }
        }
    }

    /// <summary>Saves the index atomically.</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, IndexFile);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, _index, JsonDefaults.Options, cancellationToken);
        }

        if (File.Exists(path))
        {
            File.Replace(temp, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    /// <summary>Absolute path of an item's file or folder, or <c>null</c> if it is not a safe location.</summary>
    public string? ResolvePath(ShelfItem item)
    {
        if (item.StoredPath is { } stored)
        {
            var full = Path.GetFullPath(Path.Combine(Root, stored));
            return ShelfNames.IsInside(Path.Combine(Root, ItemsFolder), full) ? full : null;
        }

        return item.LinkedPath is { } linked && Path.IsPathFullyQualified(linked) ? linked : null;
    }

    /// <summary>Adds files and folders (copy or link, per settings).</summary>
    public async Task<(IReadOnlyList<ShelfItem> Added, ShelfAddError Error)> AddPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var added = new List<ShelfItem>();
        var error = ShelfAddError.None;
        try
        {
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await AddOneAsync(path, cancellationToken);
                if (result.Item is { } item)
                {
                    added.Add(item);
                }
                else
                {
                    error = result.Error;
                }
            }
        }
        finally
        {
            await SaveAsync(CancellationToken.None);
        }

        return (added, error);
    }

    private async Task<(ShelfItem? Item, ShelfAddError Error)> AddOneAsync(string path, CancellationToken cancellationToken)
    {
        if (!ShelfNames.IsAcceptableSource(path))
        {
            return (null, ShelfAddError.NotFound);
        }

        var full = Path.GetFullPath(path);
        // Dropping the shelf itself (or a folder that contains it) would copy the copy forever.
        if (ShelfNames.IsInside(Root, full) || ShelfNames.IsInside(full, Root) || string.Equals(full.TrimEnd('\\'), Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            return (null, ShelfAddError.Failed);
        }

        var (isFolder, exists) = await Task.Run(() => (Directory.Exists(full), File.Exists(full) || Directory.Exists(full)), cancellationToken);
        if (!exists)
        {
            return (null, ShelfAddError.NotFound);
        }

        try
        {
            if (_index.Mode == ShelfMode.Link)
            {
                return (Register(new ShelfItem
                {
                    Name = ShelfNames.Sanitize(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar))),
                    Kind = isFolder ? ShelfItemKind.Folder : KindOf(full),
                    LinkedPath = full,
                    SizeBytes = isFolder ? 0 : new FileInfo(full).Length,
                }), ShelfAddError.None);
            }

            var budget = (_index.MaxSizeMb * 1024L * 1024L) - UsedBytes;
            var size = await Task.Run(() => isFolder ? FolderSize(new DirectoryInfo(full)) : new FileInfo(full).Length, cancellationToken);
            if (size > budget)
            {
                return (null, ShelfAddError.TooLarge);
            }

            var item = new ShelfItem { Kind = isFolder ? ShelfItemKind.Folder : KindOf(full), SizeBytes = size };
            var name = ShelfNames.Sanitize(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)));
            var target = TargetPath(item, name);
            try
            {
                if (isFolder)
                {
                    var copied = 0L;
                    await Task.Run(() => CopyFolder(new DirectoryInfo(full), new DirectoryInfo(target), budget, ref copied, cancellationToken), cancellationToken);
                    item.SizeBytes = copied;
                }
                else
                {
                    await using (var source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true))
                    await using (var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        item.SizeBytes = await CopyLimitedAsync(source, destination, budget, cancellationToken);
                    }

                    CopyZoneIdentifier(full, target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
            {
                DeleteItemFolder(item);
                return (null, ex is QuotaExceededException ? ShelfAddError.TooLarge : ShelfAddError.Failed);
            }

            item.Name = name;
            item.StoredPath = Path.GetRelativePath(Root, target);
            return (Register(item), ShelfAddError.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, ShelfAddError.Failed);
        }
    }

    private sealed class QuotaExceededException() : IOException("Shelf size limit reached.");

    private static async Task<long> CopyLimitedAsync(Stream source, Stream destination, long budget, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > budget)
            {
                // The file grew while we copied it.
                throw new QuotaExceededException();
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    /// <summary>Keeps the "downloaded from the internet" mark, so Windows still warns before running the copy.</summary>
    private static void CopyZoneIdentifier(string source, string target)
    {
        try
        {
            var zone = File.ReadAllText(source + ":Zone.Identifier");
            File.WriteAllText(target + ":Zone.Identifier", zone);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // No mark on the original (or a file system without streams): nothing to keep.
        }
    }

    /// <summary>Marks content that came from another app (browser, mail) as Internet zone.</summary>
    private static void MarkFromInternet(string target)
    {
        try
        {
            File.WriteAllText(target + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Best effort.
        }
    }

    /// <summary>Saves dropped text as a <c>.txt</c> file.</summary>
    public Task<ShelfItem?> AddTextAsync(string text, CancellationToken cancellationToken = default) =>
        AddContentAsync($"{Stamp("Text")}.txt", System.Text.Encoding.UTF8.GetBytes(text), ShelfItemKind.Text, cancellationToken);

    /// <summary>Saves a dropped or pasted image as PNG.</summary>
    public Task<ShelfItem?> AddImageAsync(byte[] png, CancellationToken cancellationToken = default) =>
        AddContentAsync($"{Stamp("Image")}.png", png, ShelfItemKind.Image, cancellationToken);

    /// <summary>Saves a file that came as content (browser download, mail attachment).</summary>
    public Task<ShelfItem?> AddVirtualFileAsync(string name, byte[] content, CancellationToken cancellationToken = default) =>
        AddContentAsync(ShelfNames.Sanitize(name), content, null, cancellationToken, fromInternet: true);

    /// <summary>Removes items (and their copies).</summary>
    public async Task RemoveAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        foreach (var id in ids.ToList())
        {
            if (_index.Items.FirstOrDefault(i => i.Id == id) is { } item)
            {
                DeleteItemFolder(item);
                _index.Items.Remove(item);
            }
        }

        await SaveAsync(cancellationToken);
    }

    /// <summary>Removes items older than the auto-cleanup period. Returns how many were removed.</summary>
    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        if (_index.AutoCleanupDays == 0)
        {
            return 0;
        }

        var cutoff = _time.GetUtcNow().AddDays(-_index.AutoCleanupDays);
        var old = _index.Items.Where(i => i.AddedAt < cutoff).Select(i => i.Id).ToList();
        if (old.Count > 0)
        {
            await RemoveAsync(old, cancellationToken);
        }

        return old.Count;
    }

    private async Task<ShelfItem?> AddContentAsync(string name, byte[] content, ShelfItemKind? kind, CancellationToken cancellationToken, bool fromInternet = false)
    {
        if (UsedBytes + content.Length > _index.MaxSizeMb * 1024L * 1024L)
        {
            return null;
        }

        var safe = ShelfNames.Sanitize(name);
        var item = new ShelfItem { Name = safe, Kind = kind ?? KindOf(safe), SizeBytes = content.Length };
        var target = TargetPath(item, safe);
        try
        {
            await File.WriteAllBytesAsync(target, content, cancellationToken);
            if (fromInternet)
            {
                MarkFromInternet(target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            DeleteItemFolder(item);
            return null;
        }

        item.StoredPath = Path.GetRelativePath(Root, target);
        Register(item);
        await SaveAsync(cancellationToken);
        return item;
    }

    private ShelfItem Register(ShelfItem item)
    {
        item.AddedAt = _time.GetUtcNow();
        // Display names stay unique, so dragging several items into one folder never clashes.
        item.Name = ShelfNames.Unique(item.Name, _index.Items.Select(i => i.Name));
        _index.Items.Add(item);
        return item;
    }

    private string TargetPath(ShelfItem item, string safeName)
    {
        var folder = Path.Combine(Root, ItemsFolder, item.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, safeName);
        if (!ShelfNames.IsInside(folder, target))
        {
            throw new InvalidOperationException("Unsafe shelf path.");
        }

        return target;
    }

    private void DeleteItemFolder(ShelfItem item)
    {
        var folder = Path.Combine(Root, ItemsFolder, item.Id.ToString("N"));
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by another app; the next cleanup tries again.
        }
    }

    private static bool IsValid(ShelfItem item) =>
        (item.StoredPath is not null ^ item.LinkedPath is not null)
        && (item.LinkedPath is null || ShelfNames.IsAcceptableSource(item.LinkedPath));

    private static void CopyFolder(DirectoryInfo source, DirectoryInfo target, long budget, ref long copied, CancellationToken ct)
    {
        target.Create();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Never follow junctions or symlinks: they can point anywhere (or back at us).
                continue;
            }

            // Long names cut to the same prefix must not collide.
            var name = ShelfNames.Unique(ShelfNames.Sanitize(entry.Name), taken);
            taken.Add(name);
            if (entry is DirectoryInfo dir)
            {
                CopyFolder(dir, new DirectoryInfo(Path.Combine(target.FullName, name)), budget, ref copied, ct);
            }
            else if (entry is FileInfo file)
            {
                copied += file.Length;
                if (copied > budget)
                {
                    throw new QuotaExceededException();
                }

                var destination = Path.Combine(target.FullName, name);
                file.CopyTo(destination, overwrite: false);
                CopyZoneIdentifier(file.FullName, destination);
            }
        }
    }

    private static long FolderSize(DirectoryInfo folder)
    {
        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var file in folder.EnumerateFiles("*", options))
        {
            total += file.Length;
        }

        return total;
    }

    private static ShelfItemKind KindOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".heic" or ".tif" or ".tiff" => ShelfItemKind.Image,
        ".txt" or ".md" => ShelfItemKind.Text,
        _ => ShelfItemKind.File,
    };

    private string Stamp(string prefix) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix} {_time.GetLocalNow():yyyy-MM-dd HH-mm-ss}");
}

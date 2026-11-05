using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace DynamicIsland.Modules.Translator.Services;

/// <summary>Downloads only pinned data files; never executes installers, Python or model code.</summary>
public sealed class TranslationPackageManager : ITranslationPackageManager, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TranslationModel[] _models;
    private int _disposed;
    public IReadOnlyList<TranslationPackage> AvailablePackages { get; }

    /// <summary>Takes ownership of a dedicated HTTP client configured with AllowAutoRedirect=false.</summary>
    public TranslationPackageManager(HttpClient http, string rootDirectory)
        : this(http, rootDirectory, TranslationModelCatalog.AllModels.ToArray()) { }

    internal TranslationPackageManager(HttpClient http, string rootDirectory, TranslationModel[] models)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _http = http;
        _root = Path.GetFullPath(rootDirectory);
        if (models.Any(m => m.Revision.Length == 0 || m.Revision.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ArgumentException("Invalid immutable model revision.", nameof(models));
        _models = models;
        AvailablePackages = models.GroupBy(m => m.Id).Select(g => g.First()).Select(m => new TranslationPackage(m.Source, m.Target, m.Files.Sum(f => f.Size),
            $"https://huggingface.co/{m.Repository}/tree/{m.Revision}", "CC-BY-4.0", m.Revision, m.Repository)).ToArray();
    }

    public bool IsInstalled(string sourceLanguage, string targetLanguage) => GetInstalledRevision(sourceLanguage, targetLanguage) is not null;

    public string? GetInstalledRevision(string sourceLanguage, string targetLanguage)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var model = _models.FirstOrDefault(m => m.Source == sourceLanguage && m.Target == targetLanguage);
        if (model is null) return null;
        try
        {
            if (Directory.Exists(_root)) RejectReparsePoint(_root);
            var pointer = PointerPath(model, "active");
            if (File.Exists(pointer))
            {
                RejectReparsePoint(pointer);
                var revision = File.ReadAllLines(pointer).FirstOrDefault();
                var active = _models.FirstOrDefault(m => m.Id == model.Id && m.Revision == revision);
                return active is not null && FindVersionPath(active) is not null ? revision : null;
            }
            // v0.3.1 layout remains readable and is never moved or invalidated on upgrade.
            var legacy = Path.Combine(_root, model.Id);
            return _models.FirstOrDefault(m => m.Id == model.Id && IsValidInstalled(m, legacy))?.Revision;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public bool IsUpdateAvailable(string sourceLanguage, string targetLanguage)
    {
        var latest = FindLatest(sourceLanguage, targetLanguage);
        var installed = GetInstalledRevision(sourceLanguage, targetLanguage);
        return installed is not null && installed != latest.Revision;
    }

    public bool CanRollback(string sourceLanguage, string targetLanguage)
    {
        var latest = FindLatest(sourceLanguage, targetLanguage);
        try
        {
            var previous = ReadPrevious(latest);
            return previous is not null && previous.Revision != GetInstalledRevision(sourceLanguage, targetLanguage) &&
                FindVersionPath(previous) is not null;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public string GetInstalledPath(string sourceLanguage, string targetLanguage)
    {
        var revision = GetInstalledRevision(sourceLanguage, targetLanguage)
            ?? throw new TranslationException(TranslationFailure.NotConfigured);
        var model = _models.First(m => m.Source == sourceLanguage && m.Target == targetLanguage && m.Revision == revision);
        return FindVersionPath(model) ?? throw new TranslationException(TranslationFailure.NotConfigured);
    }

    public async Task InstallAsync(string sourceLanguage, string targetLanguage,
        IProgress<TranslationPackageProgress>? progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var model = _models.FirstOrDefault(m => m.Source == sourceLanguage && m.Target == targetLanguage)
            ?? throw new TranslationException(TranslationFailure.InvalidInput);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? staging = null;
        try
        {
            Directory.CreateDirectory(_root);
            RejectReparsePoint(_root);
            using var operationLock = AcquireOperationLock();
            CleanInterruptedDownloads(model);
            if (GetInstalledRevision(sourceLanguage, targetLanguage) == model.Revision) return;
            var retained = FindVersionPath(model);
            if (retained is not null)
            {
                VerifyIntegrity(model, retained, cancellationToken);
                var retainedPrevious = GetInstalledRevision(sourceLanguage, targetLanguage);
                WriteSelection(model, model.Revision, retainedPrevious);
                return;
            }
            var total = model.Files.Sum(f => f.Size);
            var drive = new DriveInfo(Path.GetPathRoot(_root)!);
            if (drive.IsReady && drive.AvailableFreeSpace < total + 64L * 1024 * 1024)
                throw new IOException("Insufficient disk space for the language package.");
            staging = Path.Combine(_root, model.Id + ".download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            long received = 0;
            progress?.Report(new(received, total));
            foreach (var file in model.Files)
            {
                var uri = new Uri($"https://huggingface.co/{model.Repository}/resolve/{model.Revision}/{file.RemoteName}");
                using var response = await DownloadResponseAsync(uri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                ValidateOrigin(response.RequestMessage?.RequestUri);
                if (response.Content.Headers.ContentLength is { } length && length != file.Size)
                    throw new IOException("Language package size mismatch.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(Path.Combine(staging, file.LocalName), FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long fileReceived = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    fileReceived += count;
                    if (fileReceived > file.Size) throw new IOException("Language package exceeds its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, count);
                    received += count;
                    progress?.Report(new(received, total));
                }
                if (fileReceived != file.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Language package integrity verification failed.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var converter = model.Repository.StartsWith("onnx-community/", StringComparison.Ordinal) ? "ONNX Community (Xenova)" : model.Repository.Split('/')[0];
            var upstream = model.UpstreamRepository ?? $"Helsinki-NLP/opus-mt-{model.Id}";
            var attribution = $"OPUS-MT {model.Source} to {model.Target} — Helsinki-NLP/OPUS-MT; ONNX conversion and quantization by {converter}.\n" +
                $"Source: https://huggingface.co/{model.Repository}/tree/{model.Revision}\n" +
                "Converted model licence: CC BY 4.0 https://creativecommons.org/licenses/by/4.0/\n" +
                $"Upstream: https://huggingface.co/{upstream}\n" +
                "CC BY 4.0 requires attribution and an indication of conversion/quantization.\n";
            await File.WriteAllTextAsync(Path.Combine(staging, "ATTRIBUTION.txt"), attribution, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(staging, "installed"), model.Revision, cancellationToken).ConfigureAwait(false);
            var firstInstall = GetInstalledRevision(sourceLanguage, targetLanguage) is null &&
                !Directory.Exists(Path.Combine(_root, model.Id)) && !File.Exists(PointerPath(model, "active"));
            var destination = firstInstall ? Path.Combine(_root, model.Id) : RevisionPath(model);
            // Immutable revisions never replace a working model. An existing verified
            // revision can be reactivated; damaged revisions are preserved for inspection.
            if (Directory.Exists(destination))
            {
                if (!IsValidInstalled(model, destination)) throw new IOException("Existing revision is incomplete.");
                foreach (var file in model.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var input = File.OpenRead(Path.Combine(destination, file.LocalName));
                    if (!Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Existing revision integrity verification failed.");
                }
                Directory.Delete(staging, true);
            }
            else Directory.Move(staging, destination);
            staging = null;
            cancellationToken.ThrowIfCancellationRequested();
            var previous = GetInstalledRevision(sourceLanguage, targetLanguage);
            if (!firstInstall) WriteSelection(model, model.Revision, previous);

        }
        finally
        {
            try
            {
                if (staging is not null && Directory.Exists(staging)) Directory.Delete(staging, true);
            }
            finally { _gate.Release(); }
        }
    }

    public async Task RemoveAsync(string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var model = _models.FirstOrDefault(m => m.Source == sourceLanguage && m.Target == targetLanguage)
            ?? throw new TranslationException(TranslationFailure.InvalidInput);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_root)) return;
            RejectReparsePoint(_root);
            using var operationLock = AcquireOperationLock();
            CleanInterruptedDownloads(model);
            foreach (var version in _models.Where(m => m.Id == model.Id))
            {
                var path = RevisionPath(version);
                if (!Directory.Exists(path)) continue;
                RejectReparsePoint(path);
                Directory.Delete(path, true);
            }
            var legacy = Path.Combine(_root, model.Id);
            if (Directory.Exists(legacy))
            {
                RejectReparsePoint(legacy);
                Directory.Delete(legacy, true);
            }
            foreach (var kind in new[] { "active", "previous" })
            {
                var pointer = PointerPath(model, kind);
                if (!File.Exists(pointer)) continue;
                RejectReparsePoint(pointer);
                File.Delete(pointer);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task RollbackAsync(string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var latest = FindLatest(sourceLanguage, targetLanguage);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RejectReparsePoint(_root);
            using var operationLock = AcquireOperationLock();
            var previous = ReadPrevious(latest);
            var current = GetInstalledRevision(sourceLanguage, targetLanguage);
            if (previous is null || current is null || previous.Revision == current || FindVersionPath(previous) is null)
                throw new InvalidOperationException("No previous package is available.");
            // Recheck integrity before activating a retained native model.
            VerifyIntegrity(previous, FindVersionPath(previous)!, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // A single atomic pointer publishes active and previous together. If
            // replacement fails, both prior selections and directories remain intact.
            WriteSelection(latest, previous.Revision, current);
        }
        finally { _gate.Release(); }
    }

    private static void VerifyIntegrity(TranslationModel model, string path, CancellationToken cancellationToken)
    {
        foreach (var file in model.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = Path.Combine(path, file.LocalName);
            RejectReparsePoint(filePath);
            using var input = File.OpenRead(filePath);
            if (input.Length != file.Size || !Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Retained package integrity verification failed.");
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private TranslationModel FindLatest(string source, string target) =>
        _models.FirstOrDefault(m => m.Source == source && m.Target == target)
        ?? throw new TranslationException(TranslationFailure.InvalidInput);

    private string PointerPath(TranslationModel model, string kind) => Path.Combine(_root, model.Id + "." + kind);
    private string RevisionPath(TranslationModel model) => Path.Combine(_root, model.Id + ".revision-" + model.Revision);

    private string? FindVersionPath(TranslationModel model)
    {
        var version = RevisionPath(model);
        if (IsValidInstalled(model, version)) return version;
        var legacy = Path.Combine(_root, model.Id);
        return IsValidInstalled(model, legacy) ? legacy : null;
    }

    private static bool IsValidInstalled(TranslationModel model, string path)
    {
        if (!Directory.Exists(path)) return false;
        RejectReparsePoint(path);
        var marker = Path.Combine(path, "installed");
        if (!File.Exists(marker)) return false;
        RejectReparsePoint(marker);
        return File.ReadAllText(marker) == model.Revision && model.Files.All(f =>
        {
            var file = Path.Combine(path, f.LocalName);
            if (!File.Exists(file)) return false;
            RejectReparsePoint(file);
            return new FileInfo(file).Length == f.Size;
        });
    }

    private TranslationModel? ReadPrevious(TranslationModel latest)
    {
        var path = PointerPath(latest, "active");
        if (!File.Exists(path)) return null;
        RejectReparsePoint(path);
        var lines = File.ReadAllLines(path);
        return lines.Length < 2 ? null : _models.FirstOrDefault(m => m.Id == latest.Id && m.Revision == lines[1]);
    }

    private void WriteSelection(TranslationModel model, string revision, string? previous)
    {
        // Both revisions live in ONE atomically replaced pointer. An interrupted or
        // rejected replacement cannot lose the previously available rollback target.
        var path = PointerPath(model, "active");
        if (File.Exists(path)) RejectReparsePoint(path);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(System.Text.Encoding.UTF8.GetBytes(revision + "\n" + (previous ?? "")));
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<HttpResponseMessage> DownloadResponseAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects < 8; redirects++)
        {
            ValidateOrigin(uri);
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            using (response)
            {
                var location = response.Headers.Location ?? throw new IOException("Missing language package redirect location.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                ValidateOrigin(uri);
            }
        }
        throw new IOException("Too many language package redirects.");
    }

    private static void ValidateOrigin(Uri? uri)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
            !(uri.Host == "huggingface.co" || uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Unexpected language package download origin.");
    }

    private FileStream AcquireOperationLock()
    {
        var path = Path.Combine(_root, ".package-operation.lock");
        if (File.Exists(path)) RejectReparsePoint(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private void CleanInterruptedDownloads(TranslationModel model)
    {
        var prefix = model.Id + ".download-";
        foreach (var path in Directory.EnumerateDirectories(_root, prefix + "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (name.Length != prefix.Length + 32 || !Guid.TryParseExact(name[prefix.Length..], "N", out _)) continue;
            // Installer staging contains flat regular files only. Never follow/delete foreign links or trees.
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateDirectories(path).Any()) continue;
            var files = Directory.GetFiles(path);
            if (files.Any(file => (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)) continue;
            foreach (var file in files) File.Delete(file);
            Directory.Delete(path);
        }
    }

    internal static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Language package directory must not be a symbolic link.");
    }

    public void Dispose()
    {
        // HttpClient disposal aborts downloads; the managed gate remains releasable by finally.
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _http.Dispose();
    }
}

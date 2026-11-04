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
        : this(http, rootDirectory, TranslationModelCatalog.Models) { }

    internal TranslationPackageManager(HttpClient http, string rootDirectory, TranslationModel[] models)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _http = http;
        _root = Path.GetFullPath(rootDirectory);
        _models = models;
        AvailablePackages = models.Select(m => new TranslationPackage(m.Source, m.Target, m.Files.Sum(f => f.Size),
            $"https://huggingface.co/{m.Repository}", "CC-BY-4.0")).ToArray();
    }

    public bool IsInstalled(string sourceLanguage, string targetLanguage)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var model = _models.FirstOrDefault(m => m.Source == sourceLanguage && m.Target == targetLanguage);
        if (model is null) return false;
        var path = Path.Combine(_root, model.Id);
        try
        {
            return File.ReadAllText(Path.Combine(path, "installed")) == model.Revision && model.Files.All(f =>
                new FileInfo(Path.Combine(path, f.LocalName)) is { Exists: true } info && info.Length == f.Size);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public string GetInstalledPath(string sourceLanguage, string targetLanguage)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var model = _models.FirstOrDefault(m => m.Source == sourceLanguage && m.Target == targetLanguage)
            ?? throw new TranslationException(TranslationFailure.InvalidInput);
        if (!IsInstalled(sourceLanguage, targetLanguage))
            throw new TranslationException(TranslationFailure.NotConfigured);
        return Path.Combine(_root, model.Id);
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
            if (IsInstalled(sourceLanguage, targetLanguage)) return;
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
            var attribution = $"OPUS-MT {model.Source} to {model.Target} — Helsinki-NLP/OPUS-MT; ONNX conversion and quantization by ONNX Community (Xenova).\n" +
                $"Source: https://huggingface.co/{model.Repository}/tree/{model.Revision}\n" +
                "Converted model licence: CC BY 4.0 https://creativecommons.org/licenses/by/4.0/\n" +
                $"Upstream: https://huggingface.co/Helsinki-NLP/opus-mt-{model.Id} (Apache-2.0 model card).\n" +
                "Apache-2.0 licence: https://www.apache.org/licenses/LICENSE-2.0\n";
            await File.WriteAllTextAsync(Path.Combine(staging, "ATTRIBUTION.txt"), attribution, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(staging, "installed"), model.Revision, cancellationToken).ConfigureAwait(false);
            var destination = Path.Combine(_root, model.Id);
            if (Directory.Exists(destination))
            {
                RejectReparsePoint(destination);
                Directory.Delete(destination, true);
            }
            Directory.Move(staging, destination);
            staging = null;
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
            var path = Path.Combine(_root, model.Id);
            if (Directory.Exists(path))
            {
                RejectReparsePoint(path);
                Directory.Delete(path, true);
            }
        }
        finally { _gate.Release(); }
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

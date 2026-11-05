namespace DynamicIsland.Modules.Translator.Services;

/// <summary>An explicitly downloadable, immutable offline language model.</summary>
public sealed record TranslationPackage(string SourceLanguage, string TargetLanguage, long DownloadBytes, string SourceUrl, string License, string Revision = "", string ModelName = "");

public sealed record TranslationPackageProgress(long DownloadedBytes, long TotalBytes);

public interface ITranslationPackageManager
{
    IReadOnlyList<TranslationPackage> AvailablePackages { get; }
    bool IsInstalled(string sourceLanguage, string targetLanguage);
    Task InstallAsync(string sourceLanguage, string targetLanguage, IProgress<TranslationPackageProgress>? progress, CancellationToken cancellationToken);
    Task RemoveAsync(string sourceLanguage, string targetLanguage, CancellationToken cancellationToken);
    string GetInstalledPath(string sourceLanguage, string targetLanguage);
    string? GetInstalledRevision(string sourceLanguage, string targetLanguage) => null;
    bool IsUpdateAvailable(string sourceLanguage, string targetLanguage) => false;
    bool CanRollback(string sourceLanguage, string targetLanguage) => false;
    Task RollbackAsync(string sourceLanguage, string targetLanguage, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("No previous package is available."));
}

using DynamicIsland.Core.Storage;

namespace DynamicIsland.Modules.Translator.Services;

/// <summary>Local-only, bounded history; writes use the application's atomic storage.</summary>
public sealed class TranslationHistoryService(IStorageService storage, TimeProvider timeProvider) : IDisposable
{
    public const int MaximumEntries = 50;
    private const string DocumentPath = "translator-history.json";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TranslationHistoryDocument _document = new();

    public bool Enabled => _document.Enabled;
    public IReadOnlyList<TranslationHistoryEntry> Entries => _document.Entries.AsReadOnly();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await storage.LoadAsync<TranslationHistoryDocument>(DocumentPath, cancellationToken).ConfigureAwait(false);
            _document = new TranslationHistoryDocument
            {
                Enabled = loaded?.Enabled == true,
                Entries = loaded?.Entries?.Where(IsValid).Take(MaximumEntries).ToList() ?? [],
            };
        }
        finally { _gate.Release(); }
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        ChangeAsync(current => new() { Enabled = enabled, Entries = current.Entries.ToList() }, cancellationToken);

    public Task AddAsync(string source, string target, string input, string output, CancellationToken cancellationToken = default)
    {
        var entry = new TranslationHistoryEntry(Guid.NewGuid(), timeProvider.GetUtcNow(), source, target, input, output);
        if (!IsValid(entry)) { throw new ArgumentException("Invalid history entry.", nameof(input)); }
        return ChangeAsync(current => !current.Enabled ? current : new()
        {
            Enabled = true,
            Entries = new[] { entry }.Concat(current.Entries).Take(MaximumEntries).ToList(),
        }, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(current => new() { Enabled = current.Enabled, Entries = current.Entries.Where(entry => entry.Id != id).ToList() }, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        ChangeAsync(current => new() { Enabled = current.Enabled }, cancellationToken);

    private async Task ChangeAsync(Func<TranslationHistoryDocument, TranslationHistoryDocument> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = update(_document);
            if (ReferenceEquals(next, _document)) { return; }
            await storage.SaveAsync(DocumentPath, next, cancellationToken).ConfigureAwait(false);
            _document = next;
        }
        finally { _gate.Release(); }
    }

    private static bool IsValid(TranslationHistoryEntry entry) => entry is not null && entry.Id != Guid.Empty &&
        entry.SourceLanguage is "ru" or "en" && entry.TargetLanguage is "ru" or "en" &&
        !string.IsNullOrWhiteSpace(entry.Input) && entry.Input.Length <= 10000 &&
        !string.IsNullOrWhiteSpace(entry.Output) && entry.Output.Length <= 10000;

    public void Dispose() => _gate.Dispose();
}

public sealed record TranslationHistoryEntry(Guid Id, DateTimeOffset CreatedAt, string SourceLanguage, string TargetLanguage, string Input, string Output);

public sealed class TranslationHistoryDocument
{
    public bool Enabled { get; set; }
    public List<TranslationHistoryEntry> Entries { get; set; } = [];
}

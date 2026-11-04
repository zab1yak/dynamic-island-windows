using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Translator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Translator;

public sealed class TranslationHistoryTests
{
    [Fact]
    public async Task DefaultOptOut_DoesNotWriteText()
    {
        using var folder = new TempFolder();
        var storage = new JsonStorageService(folder.Path, NullLogger<JsonStorageService>.Instance);
        using var history = new TranslationHistoryService(storage, TimeProvider.System);
        await history.LoadAsync(TestContext.Current.CancellationToken);
        await history.AddAsync("en", "ru", "private input", "личный результат", TestContext.Current.CancellationToken);
        Assert.False(history.Enabled);
        Assert.Empty(history.Entries);
        Assert.False(File.Exists(storage.ResolvePath("translator-history.json")));
    }

    [Fact]
    public async Task SavedEntries_ReopenWithOriginalLanguagesTextsAndTime()
    {
        using var folder = new TempFolder();
        var storage = new JsonStorageService(folder.Path, NullLogger<JsonStorageService>.Instance);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
        using var history = new TranslationHistoryService(storage, clock);
        await history.SetEnabledAsync(true, TestContext.Current.CancellationToken);
        await history.AddAsync("en", "ru", "Hello", "Привет", TestContext.Current.CancellationToken);
        using var reopened = new TranslationHistoryService(storage, clock);
        await reopened.LoadAsync(TestContext.Current.CancellationToken);
        var entry = Assert.Single(reopened.Entries);
        Assert.True(reopened.Enabled);
        Assert.Equal("en", entry.SourceLanguage);
        Assert.Equal("ru", entry.TargetLanguage);
        Assert.Equal("Hello", entry.Input);
        Assert.Equal("Привет", entry.Output);
        Assert.Equal(clock.GetUtcNow(), entry.CreatedAt);
    }

    [Fact]
    public async Task BoundKeepsNewest_DeleteAndClearPersist()
    {
        using var folder = new TempFolder();
        var storage = new JsonStorageService(folder.Path, NullLogger<JsonStorageService>.Instance);
        using var history = new TranslationHistoryService(storage, TimeProvider.System);
        await history.SetEnabledAsync(true, TestContext.Current.CancellationToken);
        for (var index = 0; index < 53; index++)
        {
            await history.AddAsync("en", "ru", $"Input {index}", $"Вывод {index}", TestContext.Current.CancellationToken);
        }
        Assert.Equal(50, history.Entries.Count);
        Assert.Equal("Input 52", history.Entries[0].Input);
        Assert.Equal("Input 3", history.Entries[^1].Input);
        var removed = history.Entries[1].Id;
        await history.DeleteAsync(removed, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(history.Entries, entry => entry.Id == removed);
        await history.ClearAsync(TestContext.Current.CancellationToken);
        using var reopened = new TranslationHistoryService(storage, TimeProvider.System);
        await reopened.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(reopened.Entries);
        Assert.True(reopened.Enabled);
    }

    [Fact]
    public async Task OptOutStopsNewSaves_ExistingEntriesRemainUntilExplicitDeletion()
    {
        using var folder = new TempFolder();
        var storage = new JsonStorageService(folder.Path, NullLogger<JsonStorageService>.Instance);
        using var history = new TranslationHistoryService(storage, TimeProvider.System);
        await history.SetEnabledAsync(true, TestContext.Current.CancellationToken);
        await history.AddAsync("en", "ru", "One", "Один", TestContext.Current.CancellationToken);
        await history.SetEnabledAsync(false, TestContext.Current.CancellationToken);
        await history.AddAsync("en", "ru", "Two", "Два", TestContext.Current.CancellationToken);
        Assert.Equal("One", Assert.Single(history.Entries).Input);
        using var reopened = new TranslationHistoryService(storage, TimeProvider.System);
        await reopened.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(reopened.Enabled);
        Assert.Single(reopened.Entries);
    }

    [Fact]
    public async Task FailedAtomicWrite_DoesNotAdvertiseEnabledPreference()
    {
        using var history = new TranslationHistoryService(new FailingStorage(), TimeProvider.System);
        await Assert.ThrowsAsync<IOException>(() => history.SetEnabledAsync(true, TestContext.Current.CancellationToken));
        Assert.False(history.Enabled);
        Assert.Empty(history.Entries);
    }

    [Fact]
    public async Task InvalidAndExcessImportedEntries_AreFilteredAndBounded()
    {
        using var folder = new TempFolder();
        var storage = new JsonStorageService(folder.Path, NullLogger<JsonStorageService>.Instance);
        var entries = Enumerable.Range(0, 60).Select(index => new TranslationHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "en", "ru", $"Input {index}", "Вывод")).ToList();
        entries.Insert(0, new TranslationHistoryEntry(Guid.Empty, DateTimeOffset.UtcNow, "unknown", "ru", "", ""));
        await storage.SaveAsync("translator-history.json", new TranslationHistoryDocument { Enabled = true, Entries = entries }, TestContext.Current.CancellationToken);
        using var history = new TranslationHistoryService(storage, TimeProvider.System);
        await history.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(50, history.Entries.Count);
        Assert.Equal("Input 0", history.Entries[0].Input);
    }

    private sealed class FailingStorage : IStorageService
    {
        public string RootPath => string.Empty;
        public string ResolvePath(string relativePath) => relativePath;
        public Task<T?> LoadAsync<T>(string relativePath, CancellationToken cancellationToken = default) where T : class => Task.FromResult<T?>(null);
        public Task SaveAsync<T>(string relativePath, T value, CancellationToken cancellationToken = default) where T : class => throw new IOException("Test write failure");
    }
}

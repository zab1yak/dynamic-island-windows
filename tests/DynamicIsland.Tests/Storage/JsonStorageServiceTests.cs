using DynamicIsland.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.Tests.Storage;

public sealed class JsonStorageServiceTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly JsonStorageService _storage;

    public JsonStorageServiceTests()
    {
        _storage = new JsonStorageService(_temp.Path, NullLogger<JsonStorageService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    private sealed class Doc
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    [Fact]
    public async Task Load_missing_file_returns_null()
    {
        Assert.Null(await _storage.LoadAsync<Doc>("missing.json", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_then_load_round_trips_and_leaves_no_temp_file()
    {
        var ct = TestContext.Current.CancellationToken;
        await _storage.SaveAsync("a/doc.json", new Doc { Name = "Вода", Count = 3 }, ct);
        await _storage.SaveAsync("a/doc.json", new Doc { Name = "Вода", Count = 4 }, ct);

        var loaded = await _storage.LoadAsync<Doc>("a/doc.json", ct);

        Assert.NotNull(loaded);
        Assert.Equal("Вода", loaded.Name);
        Assert.Equal(4, loaded.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(_temp.Path, "a"), "*.tmp"));
    }

    [Fact]
    public async Task Corrupt_file_is_moved_aside_and_null_returned()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_temp.Path, "bad.json");
        await File.WriteAllTextAsync(path, "{ not json", ct);

        Assert.Null(await _storage.LoadAsync<Doc>("bad.json", ct));
        Assert.Single(Directory.GetFiles(_temp.Path, "bad.json.*.corrupt"));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("..\\evil.json")]
    [InlineData("a\\..\\..\\evil.json")]
    [InlineData("C:\\Windows\\evil.json")]
    public void ResolvePath_rejects_paths_outside_root(string path)
    {
        Assert.Throws<ArgumentException>(() => _storage.ResolvePath(path));
    }

    [Fact]
    public async Task Concurrent_saves_do_not_corrupt_the_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 20).Select(i => _storage.SaveAsync("c.json", new Doc { Count = i }, ct));
        await Task.WhenAll(tasks);

        var loaded = await _storage.LoadAsync<Doc>("c.json", ct);
        Assert.NotNull(loaded);
        Assert.InRange(loaded.Count, 0, 19);
    }
}

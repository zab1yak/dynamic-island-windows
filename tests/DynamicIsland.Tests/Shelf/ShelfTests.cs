using DynamicIsland.Modules.Shelf.Services;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Shelf;

public sealed class ShelfNamesTests
{
    [Theory]
    [InlineData(@"..\..\evil.txt", "evil.txt")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts", "hosts")]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("COM1.tar.gz", "_COM1.tar.gz")]
    [InlineData("LPT9", "_LPT9")]
    [InlineData("report.   ", "report")]
    [InlineData("a<b>c|d?e*f\"g.txt", "a_b_c_d_e_f_g.txt")]
    [InlineData("tab\there", "tab_here")]
    [InlineData("", "file")]
    [InlineData("...", "file")]
    [InlineData(null, "file")]
    [InlineData("ok name.docx", "ok name.docx")]
    public void Sanitize_produces_safe_single_names(string? input, string expected)
    {
        Assert.Equal(expected, ShelfNames.Sanitize(input));
    }

    [Fact]
    public void Long_names_are_cut_keeping_the_extension()
    {
        var name = ShelfNames.Sanitize(new string('a', 500) + ".pdf");
        Assert.True(name.Length <= ShelfNames.MaxLength);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Unique_adds_a_counter_case_insensitively()
    {
        Assert.Equal("a.txt", ShelfNames.Unique("a.txt", ["b.txt"]));
        Assert.Equal("a (2).txt", ShelfNames.Unique("a.txt", ["A.TXT"]));
        Assert.Equal("a (3).txt", ShelfNames.Unique("a.txt", ["a.txt", "a (2).txt"]));
    }

    [Theory]
    [InlineData(@"C:\shelf\items\x\a.txt", true)]
    [InlineData(@"C:\shelf\items\..\..\a.txt", false)]
    [InlineData(@"C:\shelf2\a.txt", false)]
    public void IsInside_blocks_traversal_and_sibling_prefixes(string path, bool inside)
    {
        Assert.Equal(inside, ShelfNames.IsInside(@"C:\shelf", path));
    }
}

public sealed class ShelfStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly string _source;
    private readonly ShelfStore _store;

    public ShelfStoreTests()
    {
        _source = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(_source);
        _store = new ShelfStore(Path.Combine(_temp.Path, "shelf"), _time);
    }

    public void Dispose() => _temp.Dispose();

    private string MakeFile(string name, int bytes = 10)
    {
        var path = Path.Combine(_source, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public async Task Copies_files_into_their_own_folder_and_survive_a_reload()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.LoadAsync(ct);
        var (added, error) = await _store.AddPathsAsync([MakeFile("photo.jpg", 100)], ct);

        Assert.Equal(ShelfAddError.None, error);
        var item = Assert.Single(added);
        Assert.Equal(ShelfItemKind.Image, item.Kind);
        var stored = _store.ResolvePath(item)!;
        Assert.True(File.Exists(stored));
        Assert.StartsWith(_store.Root, stored, StringComparison.OrdinalIgnoreCase);

        var reopened = new ShelfStore(_store.Root, _time);
        await reopened.LoadAsync(ct);
        Assert.Single(reopened.Items);
    }

    [Fact]
    public async Task Same_names_get_unique_display_names()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.LoadAsync(ct);
        var file = MakeFile("a.txt");
        await _store.AddPathsAsync([file], ct);
        await _store.AddPathsAsync([file], ct);

        Assert.Equal(["a (2).txt", "a.txt"], _store.Items.Select(i => i.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Size_limit_is_enforced()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.LoadAsync(ct);
        _store.Index.MaxSizeMb = 50;
        var big = Path.Combine(_source, "big.bin");
        await using (var f = File.Create(big))
        {
            f.SetLength(51L * 1024 * 1024);
        }

        var (added, error) = await _store.AddPathsAsync([big], ct);

        Assert.Empty(added);
        Assert.Equal(ShelfAddError.TooLarge, error);
    }

    [Fact]
    public async Task Link_mode_keeps_only_the_original_path()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.LoadAsync(ct);
        _store.Index.Mode = ShelfMode.Link;
        var file = MakeFile("doc.pdf");

        var (added, _) = await _store.AddPathsAsync([file], ct);

        Assert.Equal(file, _store.ResolvePath(added[0]));
        Assert.Equal(0, _store.UsedBytes);
    }

    [Fact]
    public async Task Folders_are_copied_recursively()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.LoadAsync(ct);
        var folder = Path.Combine(_source, "project");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        await File.WriteAllTextAsync(Path.Combine(folder, "sub", "x.txt"), "hi", ct);

        var (added, _) = await _store.AddPathsAsync([folder], ct);

        Assert.Equal(ShelfItemKind.Folder, added[0].Kind);
        Assert.True(File.Exists(Path.Combine(_store.ResolvePath(added[0])!, "sub", "x.txt")));
    }

    [Fact]
    public async Task Text_images_and_virtual_files_are_saved_with_safe_names()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.LoadAsync(ct);

        var text = await _store.AddTextAsync("hello", ct);
        var image = await _store.AddImageAsync([137, 80, 78, 71], ct);
        var virtualFile = await _store.AddVirtualFileAsync(@"..\..\..\attack.exe", [1], ct);

        Assert.EndsWith(".txt", text!.Name, StringComparison.Ordinal);
        Assert.Equal(ShelfItemKind.Image, image!.Kind);
        Assert.Equal("attack.exe", virtualFile!.Name);
        Assert.True(ShelfNames.IsInside(_store.Root, _store.ResolvePath(virtualFile)!));
    }

    [Fact]
    public async Task Remove_and_cleanup_delete_copies()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.LoadAsync(ct);
        var (added, _) = await _store.AddPathsAsync([MakeFile("old.txt")], ct);
        var path = _store.ResolvePath(added[0])!;

        _time.Advance(TimeSpan.FromDays(8));
        await _store.AddPathsAsync([MakeFile("new.txt")], ct);
        Assert.Equal(1, await _store.CleanupAsync(ct));

        Assert.False(File.Exists(path));
        Assert.Equal(["new.txt"], _store.Items.Select(i => i.Name));

        await _store.RemoveAsync(_store.Items.Select(i => i.Id), ct);
        Assert.Empty(_store.Items);
    }

    [Fact]
    public async Task Tampered_index_cannot_point_outside_the_shelf()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_store.Root);
        await File.WriteAllTextAsync(Path.Combine(_store.Root, "index.json"),
            """{ "items": [ { "name": "x", "storedPath": "..\\..\\secret.txt" } ] }""", ct);
        await File.WriteAllTextAsync(Path.Combine(_temp.Path, "secret.txt"), "s", ct);

        await _store.LoadAsync(ct);

        Assert.Empty(_store.Items);
    }
}

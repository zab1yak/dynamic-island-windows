using System.Xml.Linq;

namespace DynamicIsland.Tests.UI;

/// <summary>Every English string has a Russian translation and vice versa, in every project.</summary>
public sealed class ResxParityTests
{
    public static TheoryData<string> NeutralResxFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.resx", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.Count(c => c == '.') == 1 && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                data.Add(Path.GetRelativePath(RepoRoot(), file));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NeutralResxFiles))]
    public void Russian_resx_has_the_same_keys(string relativePath)
    {
        var neutral = Path.Combine(RepoRoot(), relativePath);
        var russian = Path.ChangeExtension(neutral, ".ru.resx");

        Assert.True(File.Exists(russian), $"Missing {russian}");
        Assert.Equal(Keys(neutral), Keys(russian));
    }

    private static string[] Keys(string path) =>
        XDocument.Load(path).Root!.Elements("data").Select(d => (string)d.Attribute("name")!).Order(StringComparer.Ordinal).ToArray();

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DynamicIsland.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}

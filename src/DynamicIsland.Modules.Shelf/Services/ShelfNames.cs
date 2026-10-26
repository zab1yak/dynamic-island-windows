using System.Text;

namespace DynamicIsland.Modules.Shelf.Services;

/// <summary>Turns untrusted names (from drops, clipboards, browsers) into safe single file names.</summary>
public static class ShelfNames
{
    /// <summary>Longest file name we create (well under MAX_PATH with the shelf prefix).</summary>
    public const int MaxLength = 120;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// A safe file name: no directory parts (so <c>..\..\x</c> becomes <c>x</c>), no invalid or control
    /// characters, no trailing dots/spaces, no reserved device names, bounded length.
    /// </summary>
    public static string Sanitize(string? name, string fallback = "file")
    {
        var raw = name ?? string.Empty;
        // Keep only the last path segment, whatever separator the source used.
        var slash = raw.LastIndexOfAny(['\\', '/', ':']);
        if (slash >= 0)
        {
            raw = raw[(slash + 1)..];
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            builder.Append(char.IsControl(c) || invalid.Contains(c) ? '_' : c);
        }

        var clean = builder.ToString().Trim().TrimEnd('.', ' ');
        if (clean.Length == 0 || clean.All(c => c is '.' or '_'))
        {
            clean = fallback;
        }

        var stem = Path.GetFileNameWithoutExtension(clean);
        var extension = Path.GetExtension(clean);
        // "CON", "con.txt", "COM1.tar.gz" are all device names to Windows.
        var device = stem.Split('.')[0];
        if (Reserved.Contains(device))
        {
            stem = "_" + stem;
        }

        if (extension.Length > 16)
        {
            extension = extension[..16];
        }

        var maxStem = Math.Max(1, MaxLength - extension.Length);
        if (stem.Length > maxStem)
        {
            stem = stem[..maxStem].TrimEnd('.', ' ');
        }

        return stem + extension;
    }

    /// <summary>
    /// Returns <paramref name="name"/>, or <c>name (2).ext</c>, <c>name (3).ext</c>… so that it does not
    /// collide with <paramref name="taken"/> (case-insensitive, like NTFS).
    /// </summary>
    public static string Unique(string name, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(name))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){extension}";
            if (!set.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Whether <paramref name="path"/> resolves inside <paramref name="root"/>.</summary>
    public static bool IsInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}

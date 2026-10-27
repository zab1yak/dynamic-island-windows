namespace DynamicIsland.Core.Status;

/// <summary>Recognizes browsers and turns their window titles into tab titles.</summary>
public static class BrowserTitles
{
    /// <summary>Longest tab title shown (the marquee scrolls the rest).</summary>
    public const int MaxLength = 160;

    private static readonly Dictionary<string, string[]> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = ["Google Chrome"],
        ["msedge"] = ["Microsoft Edge", "Microsoft​ Edge"],
        ["browser"] = ["Yandex Browser", "Яндекс Браузер", "Yandex", "Яндекс"], // Yandex Browser's process is browser.exe
        ["firefox"] = ["Mozilla Firefox", "Firefox"],
        ["opera"] = ["Opera"],
        ["brave"] = ["Brave"],
        ["vivaldi"] = ["Vivaldi"],
    };

    /// <summary>Whether <paramref name="processName"/> (without <c>.exe</c>) is a supported browser.</summary>
    public static bool IsBrowser(string? processName) =>
        processName is not null && Browsers.ContainsKey(processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName);

    /// <summary>
    /// The tab part of a browser window title: <c>"Lo-fi beats - YouTube - Google Chrome"</c> →
    /// <c>"Lo-fi beats - YouTube"</c>. Edge's "and N more pages" and profile suffixes are removed too.
    /// Returns <c>null</c> for non-browsers and empty titles.
    /// </summary>
    public static string? TabTitle(string? processName, string? windowTitle)
    {
        if (!IsBrowser(processName) || string.IsNullOrWhiteSpace(windowTitle))
        {
            return null;
        }

        var name = processName!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        var title = new string(windowTitle.Where(c => !char.IsControl(c)).ToArray()).Trim();
        foreach (var suffix in Browsers[name])
        {
            foreach (var separator in new[] { " - ", " — ", " – " })
            {
                var index = title.LastIndexOf(separator + suffix, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                {
                    title = title[..index];
                    break;
                }
            }
        }

        // Edge: "Tab and 3 more pages - Profile 1 - Microsoft Edge" → "Tab".
        foreach (var marker in new[] { " and ", " и " })
        {
            var more = title.LastIndexOf(marker, StringComparison.Ordinal);
            if (more > 0 && (title.EndsWith("pages", StringComparison.OrdinalIgnoreCase) || title.Contains(" more page", StringComparison.OrdinalIgnoreCase)
                || title.Contains("страниц", StringComparison.OrdinalIgnoreCase)))
            {
                title = title[..more];
            }
        }

        title = title.Trim();
        if (title.Length == 0 || Browsers[name].Any(s => string.Equals(s, title, StringComparison.OrdinalIgnoreCase)))
        {
            // A bare "Google Chrome" (new tab, start page) carries no information.
            return null;
        }

        return title.Length > MaxLength ? title[..MaxLength] + "…" : title;
    }
}

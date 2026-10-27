namespace DynamicIsland.Core.Status;

/// <summary>
/// Readable names for apps found in ConsentStore. Order of preference: the app's own product name
/// (exe version info or package display name), then a list of popular apps, then the cleaned-up id.
/// Windows' own components are reported as <see cref="SystemApp"/>.
/// </summary>
public static class AppNames
{
    /// <summary>Marker for Windows components; the UI shows a localized "System app".</summary>
    public const string SystemApp = "\u0001system";

    private static readonly Dictionary<string, string> KnownExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discord"] = "Discord",
        ["discordptb"] = "Discord",
        ["discordcanary"] = "Discord",
        ["telegram"] = "Telegram",
        ["zoom"] = "Zoom",
        ["ms-teams"] = "Microsoft Teams",
        ["teams"] = "Microsoft Teams",
        ["browser"] = "Яндекс Браузер",
        ["chrome"] = "Google Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Firefox",
        ["opera"] = "Opera",
        ["brave"] = "Brave",
        ["vivaldi"] = "Vivaldi",
        ["obs64"] = "OBS Studio",
        ["obs32"] = "OBS Studio",
        ["obs"] = "OBS Studio",
        ["slack"] = "Slack",
        ["skype"] = "Skype",
        ["whatsapp"] = "WhatsApp",
        ["viber"] = "Viber",
        ["webex"] = "Webex",
        ["ciscowebexstart"] = "Webex",
        ["steam"] = "Steam",
        ["steamwebhelper"] = "Steam",
        ["yandexmusic"] = "Яндекс Музыка",
        ["яндекс музыка"] = "Яндекс Музыка",
        ["spotify"] = "Spotify",
        ["audacity"] = "Audacity",
        ["vlc"] = "VLC",
        ["sharex"] = "ShareX",
        ["bdcam"] = "Bandicam",
        ["nvidia broadcast"] = "NVIDIA Broadcast",
        ["dynamicisland"] = "Dynamic Island",
    };

    private static readonly Dictionary<string, string> KnownPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.WindowsCamera"] = "Windows Camera",
        ["MSTeams"] = "Microsoft Teams",
        ["MicrosoftTeams"] = "Microsoft Teams",
        ["Microsoft.SkypeApp"] = "Skype",
        ["5319275A.WhatsAppDesktop"] = "WhatsApp",
        ["TelegramMessengerLLP.TelegramDesktop"] = "Telegram",
        ["SpotifyAB.SpotifyMusic"] = "Spotify",
        ["Microsoft.WindowsSoundRecorder"] = "Sound Recorder",
        ["Microsoft.ZuneVideo"] = "Movies & TV",
        ["Clipchamp.Clipchamp"] = "Clipchamp",
        ["Microsoft.GamingApp"] = "Xbox",
        ["Microsoft.XboxGamingOverlay"] = "Xbox Game Bar",
        ["Discord.Discord"] = "Discord",
        ["Zoom.Zoom"] = "Zoom",
    };

    private static readonly string[] SystemPackagePrefixes =
    [
        "windows.", "Microsoft.Windows.", "MicrosoftWindows.", "Microsoft.Win32WebViewHost", "Microsoft.ECApp",
        "Microsoft.AAD.", "Microsoft.AccountsControl", "Microsoft.LockApp", "Microsoft.CredDialogHost",
        "Microsoft.BioEnrollment", "Microsoft.Windows.Search", "Microsoft.Windows.ShellExperienceHost",
    ];

    /// <summary>
    /// Name for a ConsentStore entry.
    /// </summary>
    /// <param name="entry">The registry entry.</param>
    /// <param name="productName">Reads ProductName/FileDescription of an exe path; may return null.</param>
    /// <param name="packageName">Reads the display name of a package family; may return null.</param>
    public static string Resolve(ConsentEntry entry, Func<string, string?>? productName = null, Func<string, string?>? packageName = null)
    {
        if (entry.IsNonPackaged)
        {
            var path = ConsentStoreParser.ExecutablePath(entry);
            var stem = Path.GetFileNameWithoutExtension(path);
            if (IsSystemPath(path))
            {
                return SystemApp;
            }

            if (KnownExecutables.TryGetValue(stem, out var known))
            {
                return known;
            }

            return Clean(productName?.Invoke(path)) ?? (stem.Length > 0 ? stem : SystemApp);
        }

        var family = entry.KeyName;
        var underscore = family.IndexOf('_', StringComparison.Ordinal);
        var packageId = underscore > 0 ? family[..underscore] : family;
        if (KnownPackages.TryGetValue(packageId, out var name))
        {
            return name;
        }

        if (IsSystemPackage(packageId))
        {
            return SystemApp;
        }

        return Clean(packageName?.Invoke(family)) ?? ConsentStoreParser.DisplayName(entry);
    }

    /// <summary>Whether a package id belongs to Windows itself (immersive control panel, shell hosts…).</summary>
    public static bool IsSystemPackage(string packageId) =>
        packageId.Contains("immersive", StringComparison.OrdinalIgnoreCase)
        || SystemPackagePrefixes.Any(p => packageId.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsSystemPath(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return windows.Length > 0 && path.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        // Some apps put the vendor or version in the product name; keep it short and readable.
        if (trimmed.Length > 40)
        {
            trimmed = trimmed[..40].TrimEnd() + "…";
        }

        return trimmed.Contains("Microsoft® Windows® Operating System", StringComparison.OrdinalIgnoreCase) ? SystemApp : trimmed;
    }
}

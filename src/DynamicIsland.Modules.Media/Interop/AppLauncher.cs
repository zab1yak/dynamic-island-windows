using System.ComponentModel;
using System.Diagnostics;
using DynamicIsland.Modules.Media.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.Modules.Media.Interop;

/// <summary>
/// Starts a media app from its AppUserModelID, using only well-defined, safe routes:
/// 1) packaged / Start-menu apps through <c>shell:AppsFolder\&lt;AUMID&gt;</c>;
/// 2) a known URL protocol (Spotify, Yandex Music) if it is registered;
/// 3) a desktop app's exe registered under <c>App Paths</c>.
/// The AUMID is never passed to a shell command line, only as a single argument.
/// </summary>
internal sealed class AppLauncher(ILogger<AppLauncher> logger) : IAppLauncher
{
    private static readonly Dictionary<string, string> KnownProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spotify.exe"] = "spotify",
        ["SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"] = "spotify",
        ["ru.yandex.desktop.music"] = "yandexmusic",
        ["Yandex.Music"] = "yandexmusic",
    };

    /// <inheritdoc />
    public bool TryLaunch(string appId)
    {
        if (!IsPlausibleAppId(appId))
        {
            logger.LogWarning("Refusing to launch an implausible app id");
            return false;
        }

        if (appId.Contains('!', StringComparison.Ordinal) || IsInAppsFolder(appId))
        {
            return Start("explorer.exe", $@"shell:AppsFolder\{appId}");
        }

        if (KnownProtocols.TryGetValue(appId, out var protocol) && IsProtocolRegistered(protocol))
        {
            return StartShell($"{protocol}:");
        }

        if (appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && FindAppPath(appId) is { } exe)
        {
            return Start(exe, null);
        }

        logger.LogInformation("No known way to start {App}", appId);
        return false;
    }

    /// <summary>AUMIDs are short identifiers; reject paths, quotes and control characters.</summary>
    internal static bool IsPlausibleAppId(string appId) =>
        appId.Length is > 0 and <= 260
        && !appId.Any(c => char.IsControl(c) || c is '"' or '<' or '>' or '|' or '\\' or '/' or ':' or '*' or '?' or '&' or '%' or '^');

    private bool Start(string fileName, string? argument)
    {
        try
        {
            var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
            if (argument is not null)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            logger.LogWarning(ex, "Starting {File} failed", fileName);
            return false;
        }
    }

    private bool StartShell(string uri)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Opening {Uri} failed", uri);
            return false;
        }
    }

    private static bool IsProtocolRegistered(string protocol)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(protocol);
        return key?.GetValue("URL Protocol") is not null;
    }

    private static string? FindAppPath(string exeName)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            if (key?.GetValue(null) is string path)
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
                if (Path.IsPathFullyQualified(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    /// <summary>Whether a Start-menu entry with this AUMID exists (Shell.Application → AppsFolder).</summary>
    private bool IsInAppsFolder(string appId)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            foreach (var item in folder.Items())
            {
                if (string.Equals((string)item.Path, appId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            logger.LogDebug(ex, "AppsFolder lookup failed");
        }

        return false;
    }
}

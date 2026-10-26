using System.ComponentModel;
using System.Diagnostics;
using DynamicIsland.Modules.Media.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.Modules.Media.Interop;

/// <summary>
/// Starts a media app from its AppUserModelID, using only well-defined, safe routes, cheapest first:
/// 1) a known URL protocol (Spotify, Yandex Music) if it is registered;
/// 2) packaged apps (<c>Package!App</c>) through <c>shell:AppsFolder\&lt;AUMID&gt;</c>;
/// 3) a desktop app's exe registered machine-wide under <c>HKLM\…\App Paths</c> (local disk only);
/// 4) a Start-menu entry with this AUMID, found by enumerating <c>shell:AppsFolder</c>.
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

    private static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <inheritdoc />
    public Task<bool> TryLaunchAsync(string appId)
    {
        if (!IsPlausibleAppId(appId))
        {
            logger.LogWarning("Refusing to launch an implausible app id");
            return Task.FromResult(false);
        }

        // Registry and Shell enumeration can take a moment: never on the UI thread.
        return Task.Run(() => TryLaunch(appId));
    }

    private bool TryLaunch(string appId)
    {
        if (KnownProtocols.TryGetValue(appId, out var protocol) && IsProtocolRegistered(protocol))
        {
            return StartShell($"{protocol}:");
        }

        if (appId.Contains('!', StringComparison.Ordinal))
        {
            return Start(Explorer, $@"shell:AppsFolder\{appId}");
        }

        if (appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && FindAppPath(appId) is { } exe)
        {
            return Start(exe, null);
        }

        if (IsInAppsFolder(appId))
        {
            return Start(Explorer, $@"shell:AppsFolder\{appId}");
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
        // HKLM only: HKCU App Paths can be written by any process of the user.
        using var key = Registry.LocalMachine.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
        if (key?.GetValue(null) is not string raw)
        {
            return null;
        }

        var path = Environment.ExpandEnvironmentVariables(raw.Trim('"'));
        var isLocal = Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
            && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        return isLocal && File.Exists(path) ? path : null;
    }

    /// <summary>Whether a Start-menu entry with this AUMID exists (Shell.Application → AppsFolder).</summary>
    private bool IsInAppsFolder(string appId)
    {
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(type)!;
            dynamic folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            foreach (var item in folder.Items())
            {
                var path = (string)item.Path;
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(item);
                if (string.Equals(path, appId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            logger.LogDebug(ex, "AppsFolder lookup failed");
        }
        finally
        {
            if (shell is not null)
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        return false;
    }
}

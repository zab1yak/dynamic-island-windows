namespace DynamicIsland.Core.Status;

/// <summary>A privacy dot in the island.</summary>
public enum PrivacyIndicator
{
    /// <summary>Screen sharing or recording — red ring with a dot.</summary>
    ScreenShare,

    /// <summary>Camera — red dot.</summary>
    Camera,

    /// <summary>Microphone or voice chat — orange dot.</summary>
    Microphone,
}

/// <summary>Which dots to show and in what order (most important first).</summary>
public static class PrivacyIndicators
{
    /// <summary>Active indicators ordered by importance: screen share, camera, microphone.</summary>
    public static IReadOnlyList<PrivacyIndicator> Active(PrivacyStatus status)
    {
        var list = new List<PrivacyIndicator>(3);
        if (status.Recording)
        {
            list.Add(PrivacyIndicator.ScreenShare);
        }

        if (status.CameraInUse)
        {
            list.Add(PrivacyIndicator.Camera);
        }

        if (status.MicrophoneInUse)
        {
            list.Add(PrivacyIndicator.Microphone);
        }

        return list;
    }

    /// <summary>The most important indicator, or <c>null</c>.</summary>
    public static PrivacyIndicator? Primary(PrivacyStatus status) => Active(status) is [var first, ..] ? first : null;

    /// <summary>Theme brush key of an indicator (red for screen and camera, orange for the microphone).</summary>
    public static string BrushKey(PrivacyIndicator indicator) => indicator switch
    {
        PrivacyIndicator.Microphone => "Brush.Orange",
        _ => "Brush.Red",
    };
}

/// <summary>
/// Merges the microphone signals: live capture sessions (Core Audio) win because they turn off the
/// moment a stream closes; ConsentStore is used only when sessions cannot be read.
/// </summary>
public static class MicrophoneSignal
{
    /// <summary>Apps currently using a microphone.</summary>
    /// <param name="liveSessions">Apps with an active capture session, or <c>null</c> when Core Audio is unavailable.</param>
    /// <param name="consentStore">Apps ConsentStore reports as using the microphone.</param>
    public static IReadOnlyList<string> Merge(IReadOnlyList<string>? liveSessions, IReadOnlyList<string> consentStore) =>
        liveSessions is null ? consentStore : liveSessions.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>Recognizes the windows apps show while they share or record the screen.</summary>
public static class ScreenShareSignatures
{
    private static readonly string[] BrowserBarPhrases =
    [
        "is sharing your screen", "is sharing a window", "is sharing this tab", "is sharing a tab", "is sharing your entire screen",
        "доступ к вашему экрану", "доступ к окну", "доступ к этой вкладке", "демонстрирует ваш экран", "показывает ваш экран",
    ];

    private static readonly string[] Browsers = ["chrome", "msedge", "browser", "opera", "brave", "vivaldi"];

    /// <summary>Whether a visible top-level window signals screen sharing; returns the app name or <c>null</c>.</summary>
    /// <param name="processName">Owner process name without <c>.exe</c>.</param>
    /// <param name="className">Window class.</param>
    /// <param name="title">Window title.</param>
    public static string? Match(string? processName, string? className, string? title)
    {
        var process = processName ?? string.Empty;
        var text = title ?? string.Empty;
        var cls = className ?? string.Empty;

        if (Browsers.Contains(process, StringComparer.OrdinalIgnoreCase)
            && BrowserBarPhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return BrowserTitles.IsBrowser(process) ? AppNamesForBrowsers(process) : process;
        }

        if (process.Equals("Zoom", StringComparison.OrdinalIgnoreCase)
            && (cls is "ZPToolBarParentWndClass" or "ZPFloatToolbarClass" || text.Contains("share toolbar", StringComparison.OrdinalIgnoreCase)))
        {
            return "Zoom";
        }

        if (process is "ms-teams" or "Teams" or "msteams"
            && (text.Contains("Sharing control bar", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Панель управления демонстрацией", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Presenter toolbar", StringComparison.OrdinalIgnoreCase)))
        {
            return "Microsoft Teams";
        }

        return null;
    }

    /// <summary>Whether windows of <paramref name="processName"/> are worth inspecting at all.</summary>
    public static bool IsCandidate(string processName) =>
        Browsers.Contains(processName, StringComparer.OrdinalIgnoreCase)
        || processName.Equals("Zoom", StringComparison.OrdinalIgnoreCase)
        || processName is "ms-teams" or "Teams" or "msteams";

    /// <summary>Processes that run only while Windows records the screen or a game (Game Bar).</summary>
    public static IReadOnlyList<string> CaptureOnlyProcesses { get; } = ["bcastdvr"];

    private static string AppNamesForBrowsers(string process) => process.ToLowerInvariant() switch
    {
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "browser" => "Яндекс Браузер",
        "opera" => "Opera",
        "brave" => "Brave",
        "vivaldi" => "Vivaldi",
        _ => process,
    };
}

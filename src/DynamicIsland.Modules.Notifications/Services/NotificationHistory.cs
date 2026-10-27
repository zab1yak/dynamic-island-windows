using System.Runtime.InteropServices;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Modules.Notifications.Services;

/// <summary>Whether Windows lets the island read notifications.</summary>
public enum NotificationAccess
{
    /// <summary>Not asked yet.</summary>
    Unknown,

    /// <summary>Allowed.</summary>
    Allowed,

    /// <summary>The user or a policy denied access (Settings → Privacy → Notifications).</summary>
    Denied,

    /// <summary>
    /// Windows does not offer the API to this process: <c>UserNotificationListener</c> requires package
    /// identity, which the app gets only after the identity package is set up (D-038).
    /// </summary>
    Unavailable,
}

/// <summary>One notification from the Windows notification center.</summary>
public sealed record NotificationEntry(uint Id, string App, string Title, string Body, DateTimeOffset Time);

/// <summary>Reads the Windows notification center through <c>UserNotificationListener</c>.</summary>
public sealed partial class NotificationHistory
{
    private const int AppModelErrorNoPackage = 15700;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Most notifications kept on the page.</summary>
    public const int Limit = 30;

    /// <summary>Current access state.</summary>
    public NotificationAccess Access { get; private set; }

    /// <summary>Current access without any prompt (call only with package identity).</summary>
    public NotificationAccess CheckAccess()
    {
        if (!HasPackageIdentity())
        {
            return Access = NotificationAccess.Unavailable;
        }

        try
        {
            Access = UserNotificationListener.Current.GetAccessStatus() switch
            {
                UserNotificationListenerAccessStatus.Allowed => NotificationAccess.Allowed,
                UserNotificationListenerAccessStatus.Denied => NotificationAccess.Denied,
                _ => NotificationAccess.Unknown,
            };
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or TypeLoadException)
        {
            Access = NotificationAccess.Unavailable;
        }

        return Access;
    }

    /// <summary>Notifications newer than <paramref name="clearedAt"/>, newest first (what «Очистить» hid stays hidden).</summary>
    public static IReadOnlyList<NotificationEntry> Visible(IEnumerable<NotificationEntry> entries, DateTimeOffset? clearedAt) =>
        entries.Where(e => clearedAt is null || e.Time > clearedAt.Value).OrderByDescending(e => e.Time).Take(Limit).ToList();

    /// <summary>Asks Windows for access (shows the system consent prompt the first time).</summary>
    public async Task<NotificationAccess> RequestAccessAsync()
    {
        if (!HasPackageIdentity())
        {
            // The listener needs the userNotificationListener capability from an MSIX manifest.
            Access = NotificationAccess.Unavailable;
            return Access;
        }

        try
        {
            var status = await UserNotificationListener.Current.RequestAccessAsync().AsTask().WaitAsync(RequestTimeout);
            Access = status == UserNotificationListenerAccessStatus.Allowed ? NotificationAccess.Allowed : NotificationAccess.Denied;
        }
        catch (TimeoutException)
        {
            // The consent prompt is still open or Windows is slow: let the user try again.
            Access = NotificationAccess.Unknown;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or TypeLoadException)
        {
            Access = NotificationAccess.Unavailable;
        }

        return Access;
    }

    /// <summary>Latest toasts, newest first (empty without access).</summary>
    public async Task<IReadOnlyList<NotificationEntry>> ReadAsync()
    {
        if (Access != NotificationAccess.Allowed)
        {
            return [];
        }

        try
        {
            var list = await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast);
            return list
                .OrderByDescending(n => n.CreationTime)
                .Take(Limit)
                .Select(ToEntry)
                .ToList();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or TypeLoadException)
        {
            Access = NotificationAccess.Unavailable;
            return [];
        }
    }

    /// <summary>Whether this process runs with MSIX package identity.</summary>
    public static bool HasPackageIdentity()
    {
        var length = 0u;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    private static NotificationEntry ToEntry(UserNotification n)
    {
        var app = n.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty;
        var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()?.Select(t => t.Text).ToList() ?? [];
        return new NotificationEntry(n.Id, app, texts.FirstOrDefault() ?? string.Empty, string.Join(" ", texts.Skip(1)), n.CreationTime);
    }
}

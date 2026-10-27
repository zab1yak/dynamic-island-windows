using System.Runtime.InteropServices;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Modules.Tools.Services;

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
    /// Windows does not offer the API to this app: <c>UserNotificationListener</c> requires package
    /// identity (MSIX), which a Velopack desktop install does not have.
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
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or TypeLoadException or TimeoutException)
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
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
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

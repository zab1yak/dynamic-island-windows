using DynamicIsland.Core.Notifications;

namespace DynamicIsland.App.Services;

/// <summary>
/// Windows notifications through the tray icon. Works for an unpackaged app without a registered
/// AppUserModelID or Start-menu shortcut, which Windows toasts would require.
/// </summary>
internal sealed class TrayNotificationService(TrayIconService tray) : INotificationService
{
    /// <inheritdoc />
    public void Show(string title, string message, bool sound) => tray.Notify(title, message, sound);
}

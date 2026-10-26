using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using DynamicIsland.UI.Localization;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace DynamicIsland.App.Services;

/// <summary>Notification-area icon with the app menu.</summary>
internal sealed class TrayIconService(AppLifetime lifetime) : IDisposable
{
    private TaskbarIcon? _icon;
    private bool _leftClickOpensSettings;

    /// <summary>
    /// Raised when the user picks "Settings" or left-clicks the icon. The menu item appears only
    /// when a handler is attached before <see cref="Show"/>.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Creates the icon. Call on the UI thread.</summary>
    public void Show()
    {
        var menu = new ContextMenu();
        if (SettingsRequested is not null)
        {
            menu.Items.Add(CreateItem("Tray.Settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(new Separator());
            _leftClickOpensSettings = true;
        }

#if DEBUG
        menu.Items.Add(CreateItem("Tray.Showcase", () => new Debug.ShowcaseWindow().Show()));
#endif
        menu.Items.Add(CreateItem("Tray.Restart", lifetime.Restart));
        menu.Items.Add(CreateItem("Tray.Exit", lifetime.Exit));

        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/island.ico")),
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
        };
        _icon.SetBinding(TaskbarIcon.ToolTipTextProperty, new Binding("[Tray.Tooltip]") { Source = Loc.Instance });
        if (_leftClickOpensSettings)
        {
            _icon.TrayLeftMouseUp += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>Shows a quiet balloon notification (used for recovered errors).</summary>
    public void Notify(string title, string message)
    {
        _icon?.ShowNotification(title, message, NotificationIcon.None, sound: false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    private static MenuItem CreateItem(string key, Action action)
    {
        var item = new MenuItem();
        item.SetBinding(HeaderedItemsControl.HeaderProperty, new Binding($"[{key}]") { Source = Loc.Instance });
        item.Click += (_, _) => action();
        return item;
    }
}

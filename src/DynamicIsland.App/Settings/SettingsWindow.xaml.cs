using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DynamicIsland.App.Settings;

/// <summary>
/// Settings window (a normal, activatable window). Closing only hides it so the next open is instant.
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    private bool _exiting;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    /// <summary>Shows the window at <paramref name="sectionId"/> and brings it to the front.</summary>
    public void ShowSection(string? sectionId)
    {
        ViewModel.Open(sectionId);
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Really closes the window (application exit).</summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Dark title bar to match the dark content (Windows 10 20H1+).
        Platform.Windows.Windowing.WindowInterop.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>Records a key combination: modifiers plus one key.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key switch
        {
            Key.Space => "Space",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            _ => key.ToString(),
        });
        ViewModel.General.TrySetHotkey(string.Join('+', parts));
    }
}

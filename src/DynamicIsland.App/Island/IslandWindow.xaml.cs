using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Core.Animation;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Settings;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Windowing;
using DynamicIsland.UI.Animation;
using DynamicIsland.UI.Shapes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.App.Island;

/// <summary>
/// The island window. It has a fixed size (the largest island plus margins) and never moves or
/// resizes while animating: only the body geometry, the clip and the layer opacity/scale change.
/// Fully transparent pixels let clicks through to the windows below (per-pixel alpha layered window).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Owned resources are released in OnClosed, the Window lifetime hook.")]
internal sealed partial class IslandWindow : Window
{
    private const int HotkeyId = 0x1D1;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const double HorizontalScrollThreshold = 120;

    private readonly IslandStateMachine _stateMachine;
    private readonly ISettingsService _settings;
    private readonly IslandViewModel _viewModel;
    private readonly ILogger<IslandWindow> _logger;
    private readonly AnimationClock _clock;

    private readonly Spring _width;
    private readonly Spring _height;
    private readonly Spring _radius;
    private readonly Spring _shoulder;
    private readonly Spring _compactOpacity = Spring.Smooth(1);
    private readonly Spring _expandedOpacity = Spring.Smooth(0);

    private nint _hwnd;
    private GlobalHotkey? _hotkey;
    private FullscreenMonitor? _fullscreen;
    private MonitorInfo? _monitor;
    private double _horizontalScroll;
    private DateTime _lastPageFlip;
    private bool _textInput;

    public IslandWindow(IslandStateMachine stateMachine, ISettingsService settings, IslandViewModel viewModel, ILogger<IslandWindow> logger)
    {
        _stateMachine = stateMachine;
        _settings = settings;
        _viewModel = viewModel;
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;

        var compact = IslandMetrics.Compact;
        _width = Spring.Bouncy(compact.Width);
        _height = Spring.Bouncy(compact.Height);
        _radius = Spring.Bouncy(compact.BottomRadius);
        _shoulder = Spring.Bouncy(compact.Shoulder);
        _clock = new AnimationClock(Tick);

        // Root keeps the unscaled layout size; RootScale applies the user's island size setting.
        Root.Width = IslandMetrics.WindowWidth;
        Root.Height = IslandMetrics.WindowHeight;
        Root.HorizontalAlignment = HorizontalAlignment.Center;
        Root.VerticalAlignment = VerticalAlignment.Top;
        Width = IslandMetrics.WindowWidth;
        Height = IslandMetrics.WindowHeight;

        Root.MouseEnter += (_, _) => _stateMachine.PointerEntered();
        Root.MouseLeave += (_, _) => _stateMachine.PointerExited();
        Root.MouseLeftButtonUp += OnRootClicked;
        _stateMachine.ModeChanged += (_, mode) => ApplyMode(mode);
        _settings.Changed += (_, _) => ApplySettings();
        DpiChanged += (_, _) => Reposition();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        Loaded += (_, _) => Reposition();
        Render();
    }

    /// <summary>Horizontal swipe (touchpad) or Shift+wheel: +1 = next page, −1 = previous.</summary>
    public event EventHandler<int>? PageScrollRequested;

    /// <summary>Raised when the global hotkey could not be registered (taken by another app).</summary>
    public event EventHandler<string>? HotkeyUnavailable;

    /// <summary>Content of the page area of the expanded island.</summary>
    public object? PageContent
    {
        get => PageHost.Content;
        set => PageHost.Content = value;
    }

    /// <summary>
    /// Lets a text box inside the island receive keyboard input: the window becomes activatable
    /// and stays expanded until <see cref="EndTextInput"/>.
    /// </summary>
    public void BeginTextInput()
    {
        if (_textInput || _hwnd == 0)
        {
            return;
        }

        _textInput = true;
        _stateMachine.TextEditStarted();
        WindowInterop.SetActivatable(_hwnd, activatable: true);
        Activate();
    }

    /// <summary>Returns the window to its non-activating state.</summary>
    public void EndTextInput()
    {
        if (!_textInput)
        {
            return;
        }

        _textInput = false;
        WindowInterop.SetActivatable(_hwnd, activatable: false);
        _stateMachine.TextEditEnded();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowInterop.ApplyIslandStyles(_hwnd);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        _hotkey = new GlobalHotkey(_hwnd, HotkeyId);
        _hotkey.Pressed += (_, _) => _stateMachine.HotkeyPressed();
        _fullscreen = new FullscreenMonitor(_hwnd, () => _monitor);
        _fullscreen.Changed += (_, isFullscreen) =>
        {
            _logger.LogDebug("Fullscreen app on the island monitor: {Fullscreen}", isFullscreen);
            if (_settings.Current.HideInFullscreen)
            {
                _stateMachine.SetFullscreen(isFullscreen);
            }
        };

        ApplySettings();
        _fullscreen.Start();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        EndTextInput();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            EndTextInput();
            _stateMachine.Dismiss();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _clock.Stop();
        _hotkey?.Dispose();
        _fullscreen?.Dispose();
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void ApplySettings()
    {
        var s = _settings.Current;
        _stateMachine.ExpandDelay = TimeSpan.FromMilliseconds(s.HoverExpandDelayMs);
        _stateMachine.CollapseDelay = TimeSpan.FromMilliseconds(s.HoverCollapseDelayMs);
        RootScale.ScaleX = RootScale.ScaleY = s.IslandScale;
        Width = IslandMetrics.WindowWidth * s.IslandScale;
        Height = IslandMetrics.WindowHeight * s.IslandScale;
        Reposition();
        RegisterHotkey(s.ToggleHotkey);
        if (!s.HideInFullscreen)
        {
            _stateMachine.SetFullscreen(false);
        }
        else
        {
            _fullscreen?.Evaluate();
        }
    }

    private void RegisterHotkey(string text)
    {
        if (_hotkey is null || !Core.Input.HotkeyGesture.TryParse(text, out var gesture))
        {
            _logger.LogWarning("Invalid hotkey {Hotkey}", text);
            return;
        }

        if (_hotkey.Gesture == gesture)
        {
            return;
        }

        if (!_hotkey.Register(gesture.Value))
        {
            _logger.LogWarning("Hotkey {Hotkey} is taken by another application", text);
            HotkeyUnavailable?.Invoke(this, text);
        }
    }

    /// <summary>Places the window at the top centre of the chosen monitor, in physical pixels.</summary>
    private void Reposition()
    {
        if (_hwnd == 0)
        {
            return;
        }

        _monitor = MonitorService.Resolve(_settings.Current.MonitorDeviceName);
        if (_monitor is null)
        {
            _logger.LogWarning("No monitor found; island stays where it is");
            return;
        }

        var scale = _monitor.Scale;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        var x = _monitor.Bounds.Left + ((_monitor.Bounds.Width - width) / 2);
        // Keep WPF's own idea of the position in sync, otherwise it restores its default location on Show.
        Left = x / scale;
        Top = _monitor.Bounds.Top / scale;
        WindowInterop.Place(_hwnd, x, _monitor.Bounds.Top, width, height);
        _logger.LogDebug("Island placed on {Monitor} at {X},{Y} {Width}x{Height} (scale {Scale})", _monitor.DeviceName, x, _monitor.Bounds.Top, width, height, scale);
    }

    private void ApplyMode(IslandMode mode)
    {
        _logger.LogDebug("Island mode {Mode}", mode);
        _viewModel.Mode = mode;
        var size = IslandMetrics.For(mode);
        _width.Target = size.Width;
        _height.Target = size.Height;
        _radius.Target = size.BottomRadius;
        _shoulder.Target = size.Shoulder;

        var expanded = IslandMetrics.IsExpanded(mode);
        _expandedOpacity.Target = expanded ? 1 : 0;
        _compactOpacity.Target = expanded || mode == IslandMode.Hidden ? 0 : 1;
        ExpandedLayer.IsHitTestVisible = expanded;
        CompactLayer.IsHitTestVisible = !expanded;
        if (mode == IslandMode.Hidden)
        {
            EndTextInput();
        }

        _clock.Start();
    }

    private bool Tick(double dt)
    {
        var moving = false;
        moving |= _width.Step(dt);
        moving |= _height.Step(dt);
        moving |= _radius.Step(dt);
        moving |= _shoulder.Step(dt);
        // Content of the old state fades fast, the new one appears a bit slower.
        _compactOpacity.Stiffness = _compactOpacity.Target > 0 ? 260 : 900;
        _expandedOpacity.Stiffness = _expandedOpacity.Target > 0 ? 260 : 900;
        _compactOpacity.Damping = 2 * Math.Sqrt(_compactOpacity.Stiffness);
        _expandedOpacity.Damping = 2 * Math.Sqrt(_expandedOpacity.Stiffness);
        moving |= _compactOpacity.Step(dt);
        moving |= _expandedOpacity.Step(dt);
        Render();
        return moving;
    }

    private void Render()
    {
        var parameters = new IslandShapeParams(IslandMetrics.WindowWidth / 2, _width.Value, _height.Value, _radius.Value, _shoulder.Value);
        var fill = OutlineGeometry.ToGeometry(IslandShapeMath.Build(parameters), closed: true);
        Body.Data = fill;
        ContentHost.Clip = fill;
        Outline.Data = OutlineGeometry.ToGeometry(IslandShapeMath.Build(parameters, includeTopEdge: false), closed: false);

        var expanded = Math.Clamp(_expandedOpacity.Value, 0, 1);
        var compact = Math.Clamp(_compactOpacity.Value, 0, 1);
        ExpandedLayer.Opacity = expanded;
        ExpandedScale.ScaleX = ExpandedScale.ScaleY = 0.96 + (0.04 * expanded);
        CompactLayer.Opacity = compact;
        CompactScale.ScaleX = CompactScale.ScaleY = 0.96 + (0.04 * compact);
        Outline.Opacity = expanded;
        BodyShadow.Opacity = 0.45 * expanded;
        Body.Visibility = _height.Value < 0.5 ? Visibility.Hidden : Visibility.Visible;
    }

    private void OnRootClicked(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled)
        {
            _stateMachine.Clicked();
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_hotkey?.HandleMessage(msg, wParam) == true || _fullscreen?.HandleMessage(msg, wParam) == true)
        {
            handled = true;
            return 0;
        }

        switch (msg)
        {
            case WM_MOUSEHWHEEL:
                OnHorizontalWheel((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                handled = true;
                break;
            case WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(Reposition);
                break;
        }

        return 0;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            OnHorizontalWheel(-e.Delta);
            e.Handled = true;
        }
    }

    private void OnHorizontalWheel(int delta)
    {
        if (!IslandMetrics.IsExpanded(_stateMachine.Mode))
        {
            return;
        }

        _horizontalScroll += delta;
        if (Math.Abs(_horizontalScroll) < HorizontalScrollThreshold || DateTime.UtcNow - _lastPageFlip < TimeSpan.FromMilliseconds(350))
        {
            return;
        }

        PageScrollRequested?.Invoke(this, Math.Sign(_horizontalScroll));
        _horizontalScroll = 0;
        _lastPageFlip = DateTime.UtcNow;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        Reposition();
        _fullscreen?.Evaluate();
    });

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _viewModel.RefreshClock();
                Reposition();
                _fullscreen?.Evaluate();
            });
        }
    }
}

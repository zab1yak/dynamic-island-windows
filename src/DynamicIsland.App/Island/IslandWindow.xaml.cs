using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Core.Animation;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Modules;
using DynamicIsland.UI.Input;
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
    private readonly Spring _liveOpacity = Spring.Smooth(0);
    private readonly Spring _expandedOpacity = Spring.Smooth(0);

    private nint _hwnd;
    private GlobalHotkey? _hotkey;
    private FullscreenMonitor? _fullscreen;
    private MonitorInfo? _monitor;
    private double _horizontalScroll;
    private DateTime _lastPageFlip;
    private bool _textInput;
    private readonly System.Windows.Threading.DispatcherTimer _dragWatch;
    private bool _catcherOn;
    private int _lastPageIndex;
    private bool _exiting;
    private bool _closed;
    private string? _rejectedHotkey;

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
        Root.AllowDrop = true;
        Root.DragEnter += OnDragEnter;
        Root.DragOver += OnDragOver;
        Root.DragLeave += OnDragLeave;
        Root.Drop += OnDrop;
        _dragWatch = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _dragWatch.Tick += (_, _) => WatchForDrag();
        _stateMachine.ModeChanged += OnModeChanged;
        _settings.Changed += OnSettingsChanged;
        // After WM_DPICHANGED WPF applies its suggested rect; place the island once that is done.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Reposition, System.Windows.Threading.DispatcherPriority.Loaded);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        Loaded += (_, _) => Reposition();
        _lastPageIndex = viewModel.Modules.CurrentIndex;
        viewModel.Modules.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModuleHost.CurrentIndex))
            {
                SlidePage(viewModel.Modules.CurrentIndex);
            }
        };
        PageScrollRequested += (_, direction) => viewModel.Modules.Step(direction);
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

    /// <summary>Shelf (or another drop module) is available: watch for files dragged toward the island.</summary>
    public bool DropEnabled
    {
        get => _dragWatch.IsEnabled;
        set
        {
            _stateMachine.DropEnabled = value;
            if (value)
            {
                _dragWatch.Start();
            }
            else
            {
                _dragWatch.Stop();
                SetCatcher(false);
            }
        }
    }

    /// <summary>Raised with the dropped content.</summary>
    public event EventHandler<DropPayload>? Dropped;

    /// <summary>Raised when a drag with supported content reaches the island.</summary>
    public event EventHandler? DropTargetShown;

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
        if (e.Key is Key.Left or Key.Right && !_textInput)
        {
            _viewModel.Modules.Step(e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndTextInput();
            _stateMachine.Dismiss();
            e.Handled = true;
        }
    }

    /// <summary>Closes the window for real (application exit). Any other close request only hides it.</summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_exiting)
        {
            // Alt+F4 while typing in the island must not destroy the singleton window.
            e.Cancel = true;
            EndTextInput();
            _stateMachine.Dismiss();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _stateMachine.ModeChanged -= OnModeChanged;
        _settings.Changed -= OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _clock.Stop();
        _dragWatch.Stop();
        _hotkey?.Dispose();
        _fullscreen?.Dispose();
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OnModeChanged(object? sender, IslandMode mode)
    {
        if (!_closed)
        {
            ApplyMode(mode);
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!_closed)
        {
            ApplySettings();
        }
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
        else if (_fullscreen is not null)
        {
            _fullscreen.Evaluate();
            _stateMachine.SetFullscreen(_fullscreen.IsFullscreen);
        }
    }

    private void RegisterHotkey(string text)
    {
        if (_hotkey is null || !Core.Input.HotkeyGesture.TryParse(text, out var gesture))
        {
            _logger.LogWarning("Invalid hotkey {Hotkey}", text);
            return;
        }

        if (_hotkey.Gesture == gesture || _rejectedHotkey == text)
        {
            return;
        }

        var previous = _hotkey.Gesture;
        if (_hotkey.Register(gesture.Value))
        {
            _rejectedHotkey = null;
            return;
        }

        _logger.LogWarning("Hotkey {Hotkey} is taken by another application", text);
        if (previous is { } old)
        {
            // Keep the old shortcut working rather than leaving the user with none.
            _hotkey.Register(old);
        }

        if (_rejectedHotkey != text)
        {
            _rejectedHotkey = text;
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

        // Computed from constants, never from Width/Height (which WPF derives back from pixels):
        // round-tripping through doubles would grow the window by a pixel on every reposition.
        var scale = _monitor.Scale;
        var islandScale = _settings.Current.IslandScale;
        var width = (int)Math.Round(IslandMetrics.WindowWidth * islandScale * scale);
        var height = (int)Math.Round(IslandMetrics.WindowHeight * islandScale * scale);
        var x = _monitor.Bounds.Left + ((_monitor.Bounds.Width - width) / 2);
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
        _compactOpacity.Target = mode == IslandMode.Compact ? 1 : 0;
        _liveOpacity.Target = mode == IslandMode.LiveActivity ? 1 : 0;
        ExpandedLayer.IsHitTestVisible = expanded;
        CompactLayer.IsHitTestVisible = mode == IslandMode.Compact;
        LiveLayer.IsHitTestVisible = mode == IslandMode.LiveActivity;
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
        foreach (var fade in new[] { _compactOpacity, _liveOpacity, _expandedOpacity })
        {
            fade.Stiffness = fade.Target > 0 ? 260 : 900;
            fade.Damping = 2 * Math.Sqrt(fade.Stiffness);
            moving |= fade.Step(dt);
        }

        Render();
        return moving;
    }

    private void Render()
    {
        var parameters = new IslandShapeParams(IslandMetrics.WindowWidth / 2, _width.Value, _height.Value, _radius.Value, _shoulder.Value);
        var fill = OutlineGeometry.ToGeometry(IslandShapeMath.Build(parameters), closed: true);
        Body.Data = fill;
        ContentHost.Clip = fill;
        var expanded = Math.Clamp(_expandedOpacity.Value, 0, 1);
        // The hairline and the blurred shadow only exist while expanded: no cost in compact mode.
        Outline.Data = expanded > 0.01
            ? OutlineGeometry.ToGeometry(IslandShapeMath.Build(parameters, includeTopEdge: false), closed: false)
            : null;
        Body.Effect = expanded > 0.01 ? BodyShadow : null;
        var compact = Math.Clamp(_compactOpacity.Value, 0, 1);
        ExpandedLayer.Opacity = expanded;
        ExpandedScale.ScaleX = ExpandedScale.ScaleY = 0.96 + (0.04 * expanded);
        CompactLayer.Opacity = compact;
        CompactScale.ScaleX = CompactScale.ScaleY = 0.96 + (0.04 * compact);
        var live = Math.Clamp(_liveOpacity.Value, 0, 1);
        LiveLayer.Opacity = live;
        LiveScale.ScaleX = LiveScale.ScaleY = 0.96 + (0.04 * live);
        Outline.Opacity = expanded;
        Outline.Stroke = (System.Windows.Media.Brush)FindResource(_stateMachine.Mode == IslandMode.DropTarget ? "Brush.Blue" : "Island.Stroke");
        // Fully faded layers are hidden, so their animations (marquee, rolling digits) stop rendering.
        ExpandedLayer.Visibility = expanded < 0.005 ? Visibility.Hidden : Visibility.Visible;
        CompactLayer.Visibility = compact < 0.005 ? Visibility.Hidden : Visibility.Visible;
        LiveLayer.Visibility = live < 0.005 ? Visibility.Hidden : Visibility.Visible;
        BodyShadow.Opacity = 0.45 * expanded;
        Body.Visibility = _height.Value < 0.5 ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>Horizontal spring slide toward the new page.</summary>
    private void SlidePage(int index)
    {
        var direction = index >= _lastPageIndex ? 1 : -1;
        _lastPageIndex = index;
        var ease = new UI.Animation.SpringEase { DampingRatio = 0.78 };
        PageOffset.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(40 * direction, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        PageHost.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    /// <summary>
    /// Transparent pixels never receive OLE drags. While the left button is held near the island
    /// (a drag may be under way) the window gets an almost invisible background so DragEnter fires.
    /// Normal clicks are unaffected: the button is already captured by the window under the cursor.
    /// </summary>
    private void WatchForDrag()
    {
        if (_monitor is null || _hwnd == 0)
        {
            return;
        }

        var (x, y, down) = WindowInterop.GetCursor();
        var zoneHeight = 140 * _monitor.Scale;
        var zoneHalfWidth = IslandMetrics.WindowWidth / 2 * _monitor.Scale;
        var centerX = _monitor.Bounds.Left + (_monitor.Bounds.Width / 2.0);
        var near = down && y - _monitor.Bounds.Top < zoneHeight && Math.Abs(x - centerX) < zoneHalfWidth;
        SetCatcher(near || _stateMachine.Mode == IslandMode.DropTarget);
    }

    private void SetCatcher(bool on)
    {
        if (_catcherOn == on)
        {
            return;
        }

        _catcherOn = on;
        Root.Background = on ? (System.Windows.Media.Brush)FindResource("Island.DropCatcher") : null;
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!DropEnabled || !DropPayloadReader.HasSupportedData(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        if (_stateMachine.Mode != IslandMode.DropTarget)
        {
            _stateMachine.DragNear();
            DropTargetShown?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropEnabled && DropPayloadReader.HasSupportedData(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only leave when the cursor left the window.
        var p = e.GetPosition(Root);
        if (p.X <= 0 || p.Y <= 0 || p.X >= Root.ActualWidth || p.Y >= Root.ActualHeight)
        {
            _stateMachine.DragLeft();
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var payload = DropPayloadReader.Read(e.Data);
        _stateMachine.Dropped();
        SetCatcher(false);
        if (!payload.IsEmpty)
        {
            Dropped?.Invoke(this, payload);
        }
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

    /// <summary>Locked or switched-away session: stop the drag watcher; on return, re-place and re-check.</summary>
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect:
                _dragWatch.Stop();
                SetCatcher(false);
                break;
            case SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect:
                if (_stateMachine.DropEnabled)
                {
                    _dragWatch.Start();
                }

                _viewModel.RefreshClock();
                Reposition();
                _fullscreen?.Evaluate();
                break;
        }
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

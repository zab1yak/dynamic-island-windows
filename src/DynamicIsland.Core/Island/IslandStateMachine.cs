using DynamicIsland.Core.Threading;

namespace DynamicIsland.Core.Island;

/// <summary>Hover phase of the island (one explicit state instead of scattered flags).</summary>
public enum HoverPhase
{
    /// <summary>Resting; the pointer is away.</summary>
    Idle,

    /// <summary>The pointer arrived; expanding after the hover delay unless it leaves first.</summary>
    ExpandPending,

    /// <summary>Expanded by hover.</summary>
    Expanded,

    /// <summary>The pointer left; collapsing after the delay unless it comes back.</summary>
    CollapsePending,
}

/// <summary>
/// Decides the island's <see cref="IslandMode"/> from pointer, keyboard, fullscreen, drag and
/// live-activity inputs. All methods must be called on the UI thread; delayed transitions use
/// <see cref="TimeProvider"/> timers and come back through <see cref="IUiDispatcher"/>, so the
/// whole behaviour is testable with a fake clock.
/// </summary>
public sealed class IslandStateMachine : IDisposable
{
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _dispatcher;
    private ITimer? _timer;
    private int _timerGeneration;
    private bool _pointerInside;
    private bool _expanded;
    private bool _pinned;
    private bool _fullscreen;
    private bool _hasLiveActivity;
    private bool _dropping;
    private bool _editLock;

    /// <summary>Creates the machine in <see cref="IslandMode.Compact"/>.</summary>
    public IslandStateMachine(TimeProvider time, IUiDispatcher dispatcher)
    {
        _time = time;
        _dispatcher = dispatcher;
    }

    /// <summary>Raised after <see cref="Mode"/> changed.</summary>
    public event EventHandler<IslandMode>? ModeChanged;

    /// <summary>Raised when files were dropped on the island (navigate to the drop module).</summary>
    public event EventHandler? DropCompleted;

    /// <summary>Current mode.</summary>
    public IslandMode Mode { get; private set; } = IslandMode.Compact;

    /// <summary>Hover phase; at most one delayed transition is pending at any time.</summary>
    public HoverPhase Phase { get; private set; } = HoverPhase.Idle;

    /// <summary>Hover time before the island expands.</summary>
    public TimeSpan ExpandDelay { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Time after the pointer leaves before the island collapses.</summary>
    public TimeSpan CollapseDelay { get; set; } = TimeSpan.FromMilliseconds(450);

    /// <summary>Whether drag-and-drop expansion is available (the Shelf module is enabled).</summary>
    public bool DropEnabled { get; set; } = true;

    /// <summary>The pointer entered the island.</summary>
    public void PointerEntered()
    {
        if (_pointerInside && Phase is HoverPhase.ExpandPending or HoverPhase.Expanded)
        {
            // Duplicate enter (child elements, re-hit after a frame): nothing to restart.
            return;
        }

        _pointerInside = true;
        CancelTimer();
        if (_expanded)
        {
            Phase = HoverPhase.Expanded;
        }
        else if (!_fullscreen)
        {
            Phase = HoverPhase.ExpandPending;
            StartTimer(ExpandDelay, () =>
            {
                if (_pointerInside && !_fullscreen)
                {
                    _expanded = true;
                    Phase = HoverPhase.Expanded;
                }
                else
                {
                    // Fullscreen began meanwhile: forget the pending expand so the next enter starts over.
                    Phase = HoverPhase.Idle;
                }
            });
        }
    }

    /// <summary>The pointer left the island.</summary>
    public void PointerExited()
    {
        if (!_pointerInside && Phase is HoverPhase.Idle or HoverPhase.CollapsePending)
        {
            return;
        }

        _pointerInside = false;
        CancelTimer();
        Phase = HoverPhase.Idle;
        ScheduleCollapse();
    }

    /// <summary>The island background was clicked: pin, or unpin a pinned island.</summary>
    public void Clicked()
    {
        CancelTimer();
        if (_pinned)
        {
            _pinned = false;
            _expanded = _pointerInside;
        }
        else
        {
            _pinned = true;
            _expanded = true;
        }

        Update();
    }

    /// <summary>The global hotkey was pressed: toggle a pinned, expanded island.</summary>
    public void HotkeyPressed()
    {
        CancelTimer();
        _pinned = !_pinned;
        _expanded = _pinned;
        Update();
    }

    /// <summary>Collapses everything (Esc, focus lost elsewhere).</summary>
    public void Dismiss()
    {
        CancelTimer();
        Phase = HoverPhase.Idle;
        _pinned = false;
        _expanded = false;
        _dropping = false;
        Update();
    }

    /// <summary>A fullscreen app, game or presentation started or stopped.</summary>
    public void SetFullscreen(bool fullscreen)
    {
        if (_fullscreen == fullscreen)
        {
            return;
        }

        _fullscreen = fullscreen;
        if (fullscreen)
        {
            CancelTimer();
            _expanded = _pinned;
            if (Phase == HoverPhase.ExpandPending)
            {
                Phase = HoverPhase.Idle;
            }
        }

        Update();
    }

    /// <summary>Whether any live activity is going on.</summary>
    public void SetLiveActivity(bool active)
    {
        _hasLiveActivity = active;
        Update();
    }

    /// <summary>A drag with files approaches or enters the island.</summary>
    public void DragNear()
    {
        if (!DropEnabled || _fullscreen)
        {
            return;
        }

        CancelTimer();
        _dropping = true;
        Update();
    }

    /// <summary>The drag left without dropping.</summary>
    public void DragLeft()
    {
        if (!_dropping)
        {
            return;
        }

        _dropping = false;
        Update();
        ScheduleCollapse();
    }

    /// <summary>Files were dropped.</summary>
    public void Dropped()
    {
        _dropping = false;
        _expanded = true;
        Update();
        DropCompleted?.Invoke(this, EventArgs.Empty);
        ScheduleCollapse();
    }

    /// <summary>A text box got keyboard focus: keep the island open while typing.</summary>
    public void TextEditStarted()
    {
        _editLock = true;
        CancelTimer();
    }

    /// <summary>Typing finished.</summary>
    public void TextEditEnded()
    {
        _editLock = false;
        ScheduleCollapse();
    }

    /// <inheritdoc />
    public void Dispose() => CancelTimer();

    private void ScheduleCollapse()
    {
        if (!_expanded || _pinned || _pointerInside || _editLock || _dropping)
        {
            return;
        }

        Phase = HoverPhase.CollapsePending;
        StartTimer(CollapseDelay, () =>
        {
            if (!_pointerInside && !_pinned && !_editLock && !_dropping)
            {
                _expanded = false;
                Phase = HoverPhase.Idle;
            }
        });
    }

    private void StartTimer(TimeSpan delay, Action onElapsed)
    {
        CancelTimer();
        var generation = _timerGeneration;
        _timer = _time.CreateTimer(
            _ => _dispatcher.Post(() =>
            {
                if (generation != _timerGeneration)
                {
                    return;
                }

                onElapsed();
                Update();
            }),
            null,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    private void CancelTimer()
    {
        _timerGeneration++;
        _timer?.Dispose();
        _timer = null;
    }

    private void Update()
    {
        var mode = Compute();
        if (mode == Mode)
        {
            return;
        }

        Mode = mode;
        ModeChanged?.Invoke(this, mode);
    }

    private IslandMode Compute()
    {
        if (_dropping)
        {
            return IslandMode.DropTarget;
        }

        if (_pinned)
        {
            return IslandMode.Pinned;
        }

        if (_fullscreen)
        {
            return IslandMode.Hidden;
        }

        if (_expanded)
        {
            return IslandMode.Expanded;
        }

        return _hasLiveActivity ? IslandMode.LiveActivity : IslandMode.Compact;
    }
}

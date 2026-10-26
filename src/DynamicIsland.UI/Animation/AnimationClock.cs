using System.Windows.Media;

namespace DynamicIsland.UI.Animation;

/// <summary>
/// Frame clock for spring animations. It subscribes to <see cref="CompositionTarget.Rendering"/>
/// only while at least one animation is running, so an idle island renders no frames at all.
/// </summary>
public sealed class AnimationClock
{
    private const double MaxFrameSeconds = 1.0 / 20.0;

    private readonly Func<double, bool> _tick;
    private TimeSpan _lastRenderingTime;
    private bool _subscribed;

    /// <summary>Creates a clock that calls <paramref name="tick"/> with the frame time in seconds.</summary>
    /// <param name="tick">Advances all animations; returns <c>true</c> while anything still moves.</param>
    public AnimationClock(Func<double, bool> tick)
    {
        _tick = tick;
    }

    /// <summary>Whether the clock is currently receiving frames.</summary>
    public bool IsRunning => _subscribed;

    /// <summary>Starts ticking (no-op if already running). Call on the UI thread.</summary>
    public void Start()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _lastRenderingTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Stops ticking.</summary>
    public void Stop()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
        if (now == _lastRenderingTime)
        {
            // WPF can raise Rendering more than once per frame.
            return;
        }

        // The first frame after Start has no previous timestamp: assume one 60 Hz frame.
        var dt = _lastRenderingTime == TimeSpan.Zero ? 1.0 / 60.0 : (now - _lastRenderingTime).TotalSeconds;
        _lastRenderingTime = now;
        bool moving;
        try
        {
            moving = _tick(Math.Clamp(dt, 0, MaxFrameSeconds));
        }
        catch
        {
            // Never keep a failing animation subscribed: it would throw on every frame.
            Stop();
            throw;
        }

        if (!moving)
        {
            Stop();
        }
    }
}

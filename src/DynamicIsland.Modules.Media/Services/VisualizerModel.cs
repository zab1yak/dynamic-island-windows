namespace DynamicIsland.Modules.Media.Services;

/// <summary>Real output level of the default audio device.</summary>
public interface IAudioLevelSource
{
    /// <summary>Current peak 0–1.</summary>
    float ReadPeak();

    /// <summary>Forgets the device (after the default device changed).</summary>
    void Reset();
}

/// <summary>
/// Turns a stream of real peak readings into bar heights. Each bar shows the level from a slightly
/// different moment (a short history), with fast attack and slower decay, so the bars move like an
/// iOS visualizer while still following the actual sound.
/// </summary>
public sealed class VisualizerModel
{
    private const double Attack = 0.65;
    private const double Decay = 0.18;
    private const double Floor = 0.08;

    private readonly double[] _history;
    private readonly double[] _levels;
    private readonly double[] _gains;
    private int _head;

    /// <summary>Creates a model with <paramref name="bars"/> bars.</summary>
    public VisualizerModel(int bars = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bars, 1);
        _history = new double[bars * 2];
        _levels = new double[bars];
        _gains = Enumerable.Range(0, bars).Select(i => 0.75 + (0.5 * ((i * 7 % bars) / (double)Math.Max(1, bars - 1)))).ToArray();
    }

    /// <summary>Current bar heights 0–1.</summary>
    public IReadOnlyList<double> Levels => _levels;

    /// <summary>Feeds one peak sample and updates the bars.</summary>
    public void Push(double peak)
    {
        // Perceived loudness is closer to the square root of the peak amplitude.
        var value = Math.Sqrt(Math.Clamp(peak, 0, 1));
        _head = (_head + 1) % _history.Length;
        _history[_head] = value;
        for (var i = 0; i < _levels.Length; i++)
        {
            var sample = _history[(_head - (i * 2) + _history.Length) % _history.Length];
            var target = Math.Clamp(Floor + (sample * _gains[i]), Floor, 1);
            var k = target > _levels[i] ? Attack : Decay;
            _levels[i] += (target - _levels[i]) * k;
        }
    }

    /// <summary>Drops the bars to their resting height.</summary>
    public void Rest()
    {
        Array.Clear(_history);
        Array.Fill(_levels, Floor);
    }
}

using System.ComponentModel;

namespace DynamicIsland.Core.Modules;

/// <summary>
/// Whether music plays and the current bar levels of the real output peak, for the sound wave in the
/// compact island and the header. Implemented by the media module.
/// </summary>
public interface IPlaybackLevels : INotifyPropertyChanged
{
    /// <summary>Whether a track is playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Bar heights 0–1.</summary>
    IReadOnlyList<double> Levels { get; }
}

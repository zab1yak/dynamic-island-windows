namespace DynamicIsland.Core.Notifications;

/// <summary>Independent reminder sounds; never controls the user's music session.</summary>
public enum AlertSound { Pomodoro, Water }

/// <summary>Plays a bundled reminder sound through the normal Windows audio device.</summary>
public interface IAlertAudio
{
    /// <summary>Returns false if audio could not be played. System mute is respected.</summary>
    Task<bool> PlayAsync(AlertSound sound, CancellationToken cancellationToken = default);
}

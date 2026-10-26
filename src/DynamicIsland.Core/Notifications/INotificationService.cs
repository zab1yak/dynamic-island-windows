namespace DynamicIsland.Core.Notifications;

/// <summary>Shows a Windows notification (used when a timer ends or a reminder is due).</summary>
public interface INotificationService
{
    /// <summary>Shows <paramref name="title"/> and <paramref name="message"/>, optionally with the system sound.</summary>
    void Show(string title, string message, bool sound);
}

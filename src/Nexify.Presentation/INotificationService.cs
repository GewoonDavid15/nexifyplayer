namespace Nexify.Presentation;

public enum NotificationKind { Info, Success, Error }

/// <summary>In-app notifications (spec 5): a toast bottom right. Safe to call from any thread.</summary>
public interface INotificationService
{
    void Show(string message, NotificationKind kind = NotificationKind.Info);
}

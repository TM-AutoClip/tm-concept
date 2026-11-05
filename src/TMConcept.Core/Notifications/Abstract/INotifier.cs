namespace TMConcept.Core.Notifications;

/// <summary>Shows a bottom-right notification (job finished, error). The user can turn them off and choose how long they stay.</summary>
public interface INotifier
{
    /// <summary>Shows (or only logs, when notifications are off).</summary>
    void Notify(AppNotification notification);
}

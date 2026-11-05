namespace TMConcept.Core.Notifications;

/// <summary>Kind of a notification (new values are added at the end).</summary>
public enum NotificationKind
{
    /// <summary>Information (concepts ready).</summary>
    Info = 0,

    /// <summary>A job finished (video produced).</summary>
    Done = 1,

    /// <summary>Finished with a warning (video produced without sound).</summary>
    Warning = 2,

    /// <summary>A job failed.</summary>
    Failed = 3,
}

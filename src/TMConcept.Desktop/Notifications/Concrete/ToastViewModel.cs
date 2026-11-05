using TMConcept.Core.Notifications;

namespace TMConcept.Desktop.Notifications;

/// <summary>One corner notification.</summary>
public sealed class ToastViewModel(AppNotification notification, TimeSpan duration)
{
    /// <summary>Kind.</summary>
    public NotificationKind Kind => notification.Kind;

    /// <summary>Title.</summary>
    public string Title => notification.Title;

    /// <summary>Message.</summary>
    public string Message => notification.Message;

    /// <summary>How long it stays.</summary>
    public TimeSpan Duration => duration;

    /// <summary>Glyph by kind.</summary>
    public string Glyph => Kind switch
    {
        NotificationKind.Done => "✔",
        NotificationKind.Warning => "⚠",
        NotificationKind.Failed => "✖",
        _ => "ℹ",
    };

    /// <summary>Whether it is an error (red stripe).</summary>
    public bool IsError => Kind == NotificationKind.Failed;

    /// <summary>Whether it is a warning (yellow stripe).</summary>
    public bool IsWarning => Kind == NotificationKind.Warning;
}

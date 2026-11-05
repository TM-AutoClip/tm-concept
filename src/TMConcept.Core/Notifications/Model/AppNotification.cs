namespace TMConcept.Core.Notifications;

/// <summary>One notification.</summary>
public sealed record AppNotification(NotificationKind Kind, string Title, string Message);

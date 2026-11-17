namespace TMConcept.Desktop.Updates;

/// <summary>
/// One newer version in the update window: its version, its release notes as published (both languages and the SHA-256 block; the window
/// shows the part in the app's language, <see cref="ReleaseNotesText"/>) and when it was published.
/// </summary>
public sealed record VersionNotes(AppVersion Version, string Body, DateTimeOffset? PublishedAt);

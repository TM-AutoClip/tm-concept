namespace TMConcept.Storage.Backup;

/// <summary>
/// What a backup holds (shown before a restore) and what the restore brought back: exactly what was included when it was written. A
/// backup without keys means the keys are entered again on the new computer.
/// </summary>
public sealed record RestoreReport(DateTimeOffset? CreatedAtUtc, BackupContents Contents, int Keys, int Profiles, int Images, int Concepts, int Videos);

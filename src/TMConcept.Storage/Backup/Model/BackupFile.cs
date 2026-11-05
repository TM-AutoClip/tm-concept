namespace TMConcept.Storage.Backup;

/// <summary>A backup file in the backup folder.</summary>
public sealed record BackupFile(string Path, DateTimeOffset CreatedAtUtc, long SizeBytes);

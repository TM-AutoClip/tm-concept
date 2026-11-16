namespace TMConcept.Storage.Database;

/// <summary>
/// What an upgrade did: the step the file was at before (empty = a new file), the steps applied now (oldest first, empty = it was current),
/// and the copy of the file made before the first step (null when none was needed).
/// </summary>
public sealed record SchemaUpgradeResult(string From, IReadOnlyList<string> Applied, string? Backup);

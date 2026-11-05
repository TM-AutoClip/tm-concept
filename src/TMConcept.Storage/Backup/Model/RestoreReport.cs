namespace TMConcept.Storage.Backup;

/// <summary>What a restore brought back. API keys are never in a backup: they are entered again on the new computer.</summary>
public sealed record RestoreReport(int Profiles, int Concepts, int Videos);

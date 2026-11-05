namespace TMConcept.Core.Duplicates;

/// <summary>A new concept with its duplicate check result.</summary>
public sealed record DuplicateVerdict(DuplicateSubject Subject, DuplicateResult Result);

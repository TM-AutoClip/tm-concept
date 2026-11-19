namespace TMConcept.Core.Deletion;

/// <summary>
/// What a delete would take, for its confirmation: how many records of each kind and the bytes on disk (counted before anything is touched).
/// </summary>
public sealed record DeletionPlan(int Concepts, int Videos, int Documents, long Bytes);

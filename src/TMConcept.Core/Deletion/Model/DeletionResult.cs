namespace TMConcept.Core.Deletion;

/// <summary>
/// How a delete ended: how many records went (<see cref="Deleted"/>, the owners asked for: videos, concepts, images, a profile), the bytes
/// freed and what could not be removed (<see cref="Failed"/>; the record that owns such a file stays, so a second try finishes it).
/// </summary>
public sealed record DeletionResult(int Deleted, long Freed, IReadOnlyList<DeletionFailure> Failed)
{
    /// <summary>Whether everything went.</summary>
    public bool Complete => Failed.Count == 0;
}

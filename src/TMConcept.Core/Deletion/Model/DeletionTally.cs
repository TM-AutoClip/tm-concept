namespace TMConcept.Core.Deletion;

/// <summary>What a delete has done so far (records deleted, bytes freed, failures), filled while it runs.</summary>
public sealed class DeletionTally
{
    /// <summary>Records deleted.</summary>
    public int Deleted { get; set; }

    /// <summary>Bytes freed.</summary>
    public long Freed { get; set; }

    /// <summary>What could not be removed.</summary>
    public List<DeletionFailure> Failed { get; } = [];
}

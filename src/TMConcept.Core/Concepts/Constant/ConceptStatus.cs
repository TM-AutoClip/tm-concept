namespace TMConcept.Core.Concepts;

/// <summary>Where a concept is (new values are added at the end). There is no "deleted" flag: deleting from the trash is a real delete.</summary>
public enum ConceptStatus
{
    /// <summary>Generated, waiting for ✓ / ✗.</summary>
    New = 0,

    /// <summary>Approved (✓): listed on the video page.</summary>
    Approved = 1,

    /// <summary>In the production queue.</summary>
    Queued = 2,

    /// <summary>A video was produced from it.</summary>
    Produced = 3,

    /// <summary>Rejected (✗): kept in the trash for ever and still counted by the duplicate check.</summary>
    Trashed = 4,
}

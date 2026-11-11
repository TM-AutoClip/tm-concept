namespace TMConcept.Core.Concepts;

/// <summary>
/// Where a suggested topic is (new values are added at the end). It replaces the old "used" flag, which was never read (0.1.12): the
/// selected list is kept in the database, so it survives a profile switch and a restart.
/// </summary>
public enum TopicStatus
{
    /// <summary>Suggested and shown, not picked (the unticked list of a press lives only in memory).</summary>
    Offered = 0,

    /// <summary>Picked into the selected list: the next "Generate" writes one concept for it.</summary>
    Selected = 1,

    /// <summary>A concept was written for it (also when that concept was thrown away as a duplicate: the topic exists among the concepts).</summary>
    Made = 2,
}

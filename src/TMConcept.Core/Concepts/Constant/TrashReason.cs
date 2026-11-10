namespace TMConcept.Core.Concepts;

/// <summary>
/// Why a concept was moved to the trash (🗑 on a card offers the first three; new values are added at the end: the number is stored). A
/// reason makes the trashed concept a dislike the prompts send ("avoid these topics" / "avoid this kind of telling"); without one it only
/// counts as covered (no repeat).
/// </summary>
public enum TrashReason
{
    /// <summary>No reason given ("Just delete", the Delete key, or not in the trash).</summary>
    None = 0,

    /// <summary>The user did not like the topic.</summary>
    Topic = 1,

    /// <summary>The user did not like the telling (how it was told).</summary>
    Telling = 2,
}

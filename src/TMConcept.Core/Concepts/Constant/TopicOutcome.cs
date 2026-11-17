namespace TMConcept.Core.Concepts;

/// <summary>
/// What became of a suggested topic (the number is stored; new values are added at the end). There is deliberately no "rejected" value
/// (the developer, 17 Nov 2026): the user may tick 5 of 20 because those 5 are better, not because the other 15 are bad, so a topic that was
/// only shown may be suggested again later. Only a picked one (a concept was made from it) is never suggested again.
/// </summary>
public enum TopicOutcome
{
    /// <summary>Shown on the page; no concept was made from it (yet). Neutral: it may come back.</summary>
    Shown = 0,

    /// <summary>A concept was made from it: never suggested again.</summary>
    Picked = 1,
}

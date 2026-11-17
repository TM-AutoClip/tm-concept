namespace TMConcept.Core.Concepts;

/// <summary>
/// A topic "Suggest topics" once brought for a profile. Every one is kept (deleted with its profile) as memory: a picked one (a concept was
/// made from it) is never suggested again, a topic that was only shown is sent back as "recently shown, not chosen this time" and may come
/// again (the developer, 17 Nov 2026: picking 5 of 20 does not make the other 15 bad). Which topics are ticked lives on the concept page only.
/// </summary>
public sealed class TopicSuggestion
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The profile it was suggested for.</summary>
    public Guid ProfileId { get; set; }

    /// <summary>The topic as shown (one line).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>When it was suggested (UTC; the topics of one press follow each other by a tick, so they keep the answer's order).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Whether a concept was made from it (only shown = neutral).</summary>
    public TopicOutcome Outcome { get; set; } = TopicOutcome.Shown;
}

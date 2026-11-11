namespace TMConcept.Core.Concepts;

/// <summary>
/// What one concept run is asked for. The two ways of the concept page are separate types (0.1.13), so a run cannot mix them: a
/// <see cref="TopicBrief"/> is the ticked topics (exactly one concept each, no extra request, no count of its own) and an
/// <see cref="IdeaBrief"/> is a free idea with a count (never any topics). 0.1.12 had one request with a count, an extra request and a topic
/// list together: the extra request leaked into topic runs and the count was ignored once topics were there.
/// </summary>
public abstract record ConceptBrief
{
    /// <summary>How many concepts the run writes (always within <see cref="ConceptLimits.CountMin"/>–<see cref="ConceptLimits.CountMax"/>).</summary>
    public abstract int Count { get; }

    /// <summary>The text of the prompt's extra-request line and of <see cref="Runs.Run.ExtraPrompt"/> (empty = no line).</summary>
    public abstract string ExtraPrompt { get; }

    /// <summary>The topics of the batch of <paramref name="size"/> concepts at <paramref name="offset"/>; null = the prompt has no TOPICS block.</summary>
    public abstract IReadOnlyList<string>? Topics(int offset, int size);
}

namespace TMConcept.Core.Concepts;

/// <summary>
/// "Generate selected" on the concept page: exactly one concept per ticked topic, in the list's order. No extra request goes with it and
/// the count is the number of topics. None or more than <see cref="ConceptLimits.CountMax"/> is refused here rather than cut silently;
/// the page turns its button off with the reason before that.
/// </summary>
public sealed record TopicBrief : ConceptBrief
{
    /// <summary>Creates the brief of <paramref name="list"/> (1 to <see cref="ConceptLimits.CountMax"/> topics).</summary>
    public TopicBrief(IReadOnlyList<string> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentOutOfRangeException.ThrowIfLessThan(list.Count, ConceptLimits.CountMin, nameof(list));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(list.Count, ConceptLimits.CountMax, nameof(list));
        List = list.ToList();
    }

    /// <summary>The ticked topics, in order.</summary>
    public IReadOnlyList<string> List { get; }

    /// <inheritdoc />
    public override int Count => List.Count;

    /// <inheritdoc />
    public override string ExtraPrompt => string.Empty;

    /// <inheritdoc />
    public override IReadOnlyList<string> Topics(int offset, int size) => List.Skip(offset).Take(size).ToList();
}

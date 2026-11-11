namespace TMConcept.Core.Concepts;

/// <summary>
/// "Generate from an idea" on the concept page: the idea (the extra request; may be empty, then the channel alone drives the concepts)
/// and how many concepts, kept within <see cref="ConceptLimits.CountMin"/>–<see cref="ConceptLimits.CountMax"/>. Never any topics.
/// </summary>
public sealed record IdeaBrief(string Idea, int Requested) : ConceptBrief
{
    /// <inheritdoc />
    public override int Count => Math.Clamp(Requested, ConceptLimits.CountMin, ConceptLimits.CountMax);

    /// <inheritdoc />
    public override string ExtraPrompt => (Idea ?? string.Empty).Trim();

    /// <inheritdoc />
    public override IReadOnlyList<string>? Topics(int offset, int size) => null;
}

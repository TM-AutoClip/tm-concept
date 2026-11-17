using TMConcept.Core.Documents;

namespace TMConcept.Core.Concepts;

/// <summary>
/// "Araştırmadan" on the concept page (0.1.22): <see cref="Requested"/> concepts about <see cref="Subject"/> that rest only on what the
/// research fetched and saved into the profile's documents (<see cref="DocumentIds"/>; set by the section once the pages are saved, empty
/// before, when only a price is asked). The concepts are written in the profile's language whatever language the pages are in, and name the
/// pages they use, like "Belgelerden". <see cref="Phase"/>: where the passages start in each page (0–1, another one at every press).
/// </summary>
public sealed record ResearchBrief : ConceptBrief
{
    /// <summary>Creates the brief.</summary>
    public ResearchBrief(string subject, int requested)
    {
        Subject = (subject ?? string.Empty).Trim();
        Requested = requested;
    }

    /// <summary>The subject the research was about.</summary>
    public string Subject { get; }

    /// <summary>How many concepts.</summary>
    public int Requested { get; }

    /// <summary>The documents the research saved (empty = not fetched yet; such a brief can only be priced).</summary>
    public IReadOnlyList<Guid> DocumentIds { get; init; } = [];

    /// <summary>Where the passages start in each page (0–1).</summary>
    public double Phase { get; init; }

    /// <inheritdoc />
    public override int Count => Math.Clamp(Requested, ConceptLimits.CountMin, DocumentLimits.ConceptCountMax);

    /// <inheritdoc />
    public override string ExtraPrompt => Subject;

    /// <inheritdoc />
    public override bool Grounded => true;

    /// <inheritdoc />
    public override IReadOnlyList<string>? Topics(int offset, int size) => null;
}

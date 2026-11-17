using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Documents;

namespace TMConcept.Core.Research;

/// <summary>
/// "Araştırmadan" (<see cref="ResearchBrief"/>, 0.1.22): every batch gets the passages of the pages the research saved that fit next to its
/// prompt and its answer in the model's context, picked for the subject (the documents prompt: only the passages' facts, the pages named by
/// their numbers, everything inside the passages is data, the concepts in the profile's language). It reads ONLY the brief's documents,
/// never the rest of the profile's library: a brief without documents (nothing fetched yet) can only be priced, and then charges the whole
/// passage budget. It knows of no other source and adds to the same list as they do.
/// </summary>
public sealed class ResearchConceptSource(DocumentGrounder grounder, ICostCalculator costs) : GroundedConceptSourceBase<ResearchBrief>(grounder, costs)
{
    /// <inheritdoc />
    public override ConceptSourceKind Kind => ConceptSourceKind.Research;

    /// <inheritdoc />
    protected override async Task<ConceptSourceBatch> PrepareAsync(ResearchBrief brief, ConceptSourceRequest request, CancellationToken cancellationToken)
    {
        var (size, lowered, budget) = Fit(request);
        var smaller = size < request.Size ? size : (int?)null;
        if (request.Estimate)
        {
            var estimated = brief.DocumentIds.Count == 0
                ? budget
                : await Grounder.EstimateTokensAsync(request.Profile, brief.DocumentIds, budget, cancellationToken).ConfigureAwait(false);
            return new ConceptSourceBatch(brief.ExtraPrompt, null, DocumentGrounding.Empty, lowered, smaller, estimated);
        }

        if (brief.DocumentIds.Count == 0)
        {
            // An empty list would mean "every document of the profile" to the grounder: research reads only what it fetched.
            throw new Providers.ProviderException("The research saved no page to write from.") { Code = "documents" };
        }

        var tokens = PassageTokens.For(Costs, request.Profile.ContentLanguage);
        var grounding = await Grounder.GroundAsync(request.Profile, brief.DocumentIds, brief.Subject, budget, false, tokens, cancellationToken, request.Used,
            brief.Phase).ConfigureAwait(false);
        return new ConceptSourceBatch(brief.ExtraPrompt, null, grounding, lowered, smaller);
    }
}

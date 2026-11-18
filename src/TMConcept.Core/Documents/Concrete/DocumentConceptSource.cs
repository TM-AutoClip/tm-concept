using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;

namespace TMConcept.Core.Documents;

/// <summary>
/// "Belgelerden" (<see cref="DocumentBrief"/>): every batch gets the passages of the chosen documents that fit next to its prompt and its
/// answer in the model's context (<see cref="DocumentBudget"/>; a small local context gets a lower answer limit and, when even that leaves no
/// room, fewer concepts per call: the batch says its smaller size and the generator splits the rest), picked for the subject and the
/// batch's topics (<see cref="DocumentGrounder"/>). With topics each concept is one call, so each topic gets its own passages: a found topic
/// is searched in the documents it was found in, with its search words in the documents' language. Passages earlier batches of the run sent
/// come last, so a long run reads further into the documents. Passages are counted on the safe side for any script
/// (<see cref="PassageTokens"/>). An estimate reads no passage: it charges the budget (<see cref="DocumentGrounder.EstimateTokensAsync"/>).
/// Whether the model is online or local only changes the numbers.
/// </summary>
public sealed class DocumentConceptSource(DocumentGrounder grounder, ICostCalculator costs) : GroundedConceptSourceBase<DocumentBrief>(grounder, costs)
{
    /// <inheritdoc />
    public override ConceptSourceKind Kind => ConceptSourceKind.Documents;

    /// <inheritdoc />
    public override int BatchSize(ConceptBrief brief, CatalogModel model, int fitting, double perConceptHigh) =>
        brief is DocumentBrief { TopicList.Count: > 0 } ? 1 : base.BatchSize(brief, model, fitting, perConceptHigh);

    /// <inheritdoc />
    protected override async Task<ConceptSourceBatch> PrepareAsync(DocumentBrief brief, ConceptSourceRequest request, CancellationToken cancellationToken)
    {
        var (size, lowered, budget) = Fit(request);
        var topics = brief.Topics(request.Offset, size);
        var found = topics is { Count: 1 } ? brief.Topic(request.Offset) : null;
        var chosen = await ChosenAsync(request.Profile.Id, brief.DocumentIds, found, cancellationToken).ConfigureAwait(false);
        if (request.Estimate)
        {
            var estimated = await Grounder.EstimateTokensAsync(request.Profile, chosen, budget, cancellationToken, brief.Pages, brief.Digest).ConfigureAwait(false);
            return new ConceptSourceBatch(brief.ExtraPrompt, topics, DocumentGrounding.Empty, lowered, size < request.Size ? size : null, estimated);
        }

        var query = string.Join(' ', new[] { brief.Subject }.Concat(topics ?? []).Concat(found?.Keywords ?? []).Where(t => t.Length > 0));
        var tokens = PassageTokens.For(Costs, request.Profile.ContentLanguage);
        var grounding = await Grounder.GroundAsync(request.Profile, chosen, query, budget, false, tokens, cancellationToken, request.Used, brief.Phase, brief.Pages,
            brief.Digest).ConfigureAwait(false);
        if (!grounding.Matched && !ReferenceEquals(chosen, brief.DocumentIds))
        {
            // Nothing about the topic in the documents it was said to come from (a wrong number in the answer): every chosen one is searched.
            grounding = await Grounder.GroundAsync(request.Profile, brief.DocumentIds, query, budget, false, tokens, cancellationToken, request.Used, brief.Phase,
                brief.Pages, brief.Digest).ConfigureAwait(false);
        }
        return new ConceptSourceBatch(brief.ExtraPrompt, topics, grounding, lowered, size < request.Size ? size : null);
    }

    /// <summary>
    /// The documents to search: a found topic's own documents among the chosen ones, when one of them is still ready; otherwise every chosen
    /// one (a topic without documents, or whose documents were deleted or unticked since).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ChosenAsync(Guid profileId, IReadOnlyList<Guid> chosen, DocumentTopic? topic, CancellationToken cancellationToken)
    {
        if (topic is not { DocumentIds.Count: > 0 })
        {
            return chosen;
        }

        var own = chosen.Count == 0 ? topic.DocumentIds.ToList() : topic.DocumentIds.Where(chosen.Contains).ToList();
        if (own.Count == 0)
        {
            return chosen;
        }

        var ready = await Grounder.ReadyAsync(profileId, own, cancellationToken).ConfigureAwait(false);
        return ready.Count > 0 ? ready.Select(d => d.Id).ToList() : chosen;
    }
}

using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;

namespace TMConcept.Core.Documents;

/// <summary>
/// "Belgelerden" (<see cref="DocumentBrief"/>): every batch gets the passages of the chosen documents that fit next to its prompt and its
/// answer in the model's context (<see cref="DocumentBudget"/>; a small local context gets a lower answer limit and fewer concepts per call),
/// picked for the subject and the batch's topics (<see cref="DocumentGrounder"/>). With topics each concept is one call, so each topic gets
/// its own passages. Whether the model is online or local only changes the numbers.
/// </summary>
public sealed class DocumentConceptSource(DocumentGrounder grounder, ICostCalculator costs) : ConceptSourceBase<DocumentBrief>
{
    /// <inheritdoc />
    public override ConceptSourceKind Kind => ConceptSourceKind.Documents;

    /// <inheritdoc />
    public override int BatchSize(ConceptBrief brief, CatalogModel model, int fitting, double perConceptHigh) =>
        brief is DocumentBrief { TopicList.Count: > 0 } ? 1 : DocumentBudget.BatchSize(model, fitting, perConceptHigh);

    /// <inheritdoc />
    protected override async Task<ConceptSourceBatch> PrepareAsync(DocumentBrief brief, ConceptSourceRequest request, CancellationToken cancellationToken)
    {
        var lowered = DocumentBudget.LoweredOutput(request.Model, request.PromptTokens, request.OutputTokens, request.MaxOutputTokens);
        var budget = DocumentBudget.PassageTokens(request.Model, request.PromptTokens, lowered ?? request.MaxOutputTokens);
        if (budget == 0)
        {
            throw new Providers.ProviderException("The model's context leaves no room for document passages.") { Code = "context" };
        }

        var topics = brief.Topics(request.Offset, request.Size);
        var query = string.Join(' ', new[] { brief.Subject }.Concat(topics ?? []).Where(t => t.Length > 0));
        var language = request.Profile.ContentLanguage;
        var grounding = await grounder.GroundAsync(request.Profile, brief.DocumentIds, query, budget, request.Estimate, text => costs.Tokens(text, language),
            cancellationToken).ConfigureAwait(false);
        return new ConceptSourceBatch(brief.ExtraPrompt, topics, grounding, lowered);
    }
}

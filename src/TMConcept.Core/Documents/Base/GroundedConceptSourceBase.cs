using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;

namespace TMConcept.Core.Documents;

/// <summary>
/// The common part of a source whose concepts rest on document passages ("Belgelerden" 0.1.21, "Araştırmadan" 0.1.22): the passages are
/// picked by <see cref="Grounder"/> and fitted next to the prompt and the answer in the model's context (<see cref="Fit"/>: a small local
/// context gets a lower answer limit and, when even that leaves no room, fewer concepts per call), counted on the safe side for any script
/// (<see cref="PassageTokens"/>). Whether the model is online or local only changes the numbers.
/// </summary>
public abstract class GroundedConceptSourceBase<TBrief>(DocumentGrounder grounder, ICostCalculator costs) : ConceptSourceBase<TBrief>
    where TBrief : ConceptBrief
{
    /// <summary>Picks the passages.</summary>
    protected DocumentGrounder Grounder { get; } = grounder;

    /// <summary>Counts tokens.</summary>
    protected ICostCalculator Costs { get; } = costs;

    /// <inheritdoc />
    public override int BatchSize(ConceptBrief brief, CatalogModel model, int fitting, double perConceptHigh) => DocumentBudget.BatchSize(model, fitting, perConceptHigh);

    /// <summary>
    /// The batch's size, its lowered answer limit (null = the usual one) and its passage budget: the asked size when it leaves room for
    /// passages, else the largest smaller one that does (its expected output shrinks with it). <see cref="Providers.ProviderException"/> with
    /// the code <c>context</c> when not even one concept leaves room.
    /// </summary>
    protected static (int Size, int? Lowered, int Budget) Fit(ConceptSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        for (var size = request.Size; size >= 1; size--)
        {
            var output = (long)Math.Ceiling(request.OutputTokens * (double)size / Math.Max(1, request.Size));
            var lowered = DocumentBudget.LoweredOutput(request.Model, request.PromptTokens, output, request.MaxOutputTokens);
            var budget = DocumentBudget.PassageTokens(request.Model, request.PromptTokens, lowered ?? request.MaxOutputTokens);
            if (budget > 0)
            {
                return (size, lowered, budget);
            }
        }

        throw new Providers.ProviderException("The model's context leaves no room for document passages.") { Code = "context" };
    }
}

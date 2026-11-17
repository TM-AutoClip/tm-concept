using TMConcept.Core.Models;

namespace TMConcept.Core.Documents;

/// <summary>
/// How many tokens of document passages one call can carry: the model's context minus the prompt, the answer and a
/// <see cref="DocumentLimits.ContextMargin"/> margin; at most <see cref="DocumentLimits.PassageTokensOnlineMax"/> for a paid model (a big
/// context costs money); 0 (the run is refused with the reason) when fewer than <see cref="DocumentLimits.PassageTokensMin"/> would fit. A
/// small local context (8 192 tokens) gets its answer limit lowered to the batch's own need first (<see cref="LoweredOutput"/>), which
/// leaves it about 3–4 thousand tokens of passages; an unknown context counts as <see cref="UnknownContext"/>.
/// </summary>
public static class DocumentBudget
{
    /// <summary>The context of a model that does not say.</summary>
    public const long UnknownContext = 128_000;

    /// <summary>Most passage tokens of a free (local) model in one call: more only makes the prompt slow, not better.</summary>
    public const int LocalMax = 24_000;

    /// <summary>Prompt tokens a documents call takes besides its passages, about (the batch-size estimate before a prompt exists).</summary>
    public const int PromptAllowance = 3_000;

    /// <summary>The context of <paramref name="model"/> (or <see cref="UnknownContext"/>).</summary>
    public static long Context(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.ContextTokens > 0 ? model.ContextTokens : UnknownContext;
    }

    /// <summary>Whether a call of <paramref name="model"/> costs money.</summary>
    public static bool Paid(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.InputUsd > 0 || model.OutputUsd > 0;
    }

    /// <summary>
    /// A lower answer limit when the usual one (<paramref name="maxOutput"/>) would leave fewer than the fewest passage tokens next to a prompt of
    /// <paramref name="promptTokens"/>: the batch's own expected output (<paramref name="outputTokens"/>, at least 256). Null = keep the usual one.
    /// </summary>
    public static int? LoweredOutput(CatalogModel model, long promptTokens, long outputTokens, int maxOutput)
    {
        var context = Context(model);
        if (Room(context, promptTokens, maxOutput) >= DocumentLimits.PassageTokensMin)
        {
            return null;
        }

        return (int)Math.Clamp(outputTokens, 256, Math.Max(256, maxOutput));
    }

    /// <summary>Passage tokens of one call with an answer limit of <paramref name="maxOutput"/> (0 = too little room).</summary>
    public static int PassageTokens(CatalogModel model, long promptTokens, int maxOutput)
    {
        var room = Room(Context(model), promptTokens, maxOutput);
        if (room < DocumentLimits.PassageTokensMin)
        {
            return 0;
        }

        return (int)Math.Min(room, Paid(model) ? DocumentLimits.PassageTokensOnlineMax : LocalMax);
    }

    /// <summary>Concepts per call that leave room for passages when one takes <paramref name="perConceptHigh"/> output tokens (at least 1).</summary>
    public static int BatchSize(CatalogModel model, int fitting, double perConceptHigh)
    {
        var context = Context(model);
        var free = (context * (1 - DocumentLimits.ContextMargin)) - PromptAllowance - DocumentLimits.PassageTokensMin;
        var bySpace = perConceptHigh <= 0 ? fitting : (int)Math.Floor(free / perConceptHigh);
        return Math.Clamp(Math.Min(fitting, bySpace), 1, Concepts.ConceptLimits.BatchSize);
    }

    private static long Room(long context, long promptTokens, long output) =>
        context - promptTokens - output - (long)(context * DocumentLimits.ContextMargin);
}

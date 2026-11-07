using TMConcept.Core.Chat;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Costs;

/// <summary>
/// <see cref="ICostCalculator"/>. Prices are per one million tokens; cache reads and writes use their own prices (falling back to the input
/// price); a prompt above the model's long-context threshold is billed at the long-context prices for input and output.
/// </summary>
public sealed class CostCalculator : ICostCalculator
{
    private const decimal Million = 1_000_000m;

    /// <inheritdoc />
    public decimal Exact(CatalogModel model, ChatUsage usage, decimal? reportedUsd = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(usage);
        if (reportedUsd is { } reported)
        {
            return reported;
        }

        var (input, output) = Prices(model, usage.Prompt);
        var read = model.CacheReadUsd > 0 ? model.CacheReadUsd : input;
        var write = model.CacheWriteUsd > 0 ? model.CacheWriteUsd : input;
        return ((usage.Input * input) + (usage.CacheRead * read) + (usage.CacheWrite * write) + (usage.Output * output)) / Million;
    }

    /// <inheritdoc />
    public CostEstimate EstimateChat(CatalogModel model, long promptTokens, long cachedTokens, long outputMin, long outputMax)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (input, output) = Prices(model, promptTokens);
        var read = model.CacheReadUsd > 0 ? model.CacheReadUsd : input;
        var write = model.CacheWriteUsd > 0 ? model.CacheWriteUsd : input;
        var cached = Math.Clamp(cachedTokens, 0, promptTokens);
        var low = (((promptTokens - cached) * input) + (cached * read) + (outputMin * output)) / Million;
        var high = (((promptTokens - cached) * input) + (cached * Math.Max(write, input)) + (outputMax * output)) / Million;
        return new CostEstimate(low, Math.Max(low, high), promptTokens, outputMin, outputMax);
    }

    /// <inheritdoc />
    public long Tokens(string text, string language)
    {
        return (long)Math.Ceiling((text ?? string.Empty).Length / OutputLanguages.Of(language).CharactersPerToken);
    }

    private static (decimal Input, decimal Output) Prices(CatalogModel model, long promptTokens) =>
        model.LongContextTokens > 0 && promptTokens > model.LongContextTokens && model.LongInputUsd > 0
            ? (model.LongInputUsd, model.LongOutputUsd > 0 ? model.LongOutputUsd : model.OutputUsd)
            : (model.InputUsd, model.OutputUsd);
}

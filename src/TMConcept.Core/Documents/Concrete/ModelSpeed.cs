using TMConcept.Core.Costs;
using TMConcept.Core.Models;

namespace TMConcept.Core.Documents;

/// <summary>
/// Rough speeds for the time estimates of document work (0.1.24; never a promise). An online model: about 2 s before the answer, 4,000
/// prompt tokens and 70 answer tokens a second. A model on this computer: its weights' size (the graphics card class it needs, else its
/// memory, else 5 GB) against the graphics card: on the card, answer tokens ≈ 350 / GB and prompt tokens ≈ 8,000 / GB a second; on the
/// processor, ≈ 40 / GB and ≈ 400 / GB. The time range is the estimate's tokens at these speeds, from the low to the high answer length,
/// with the high end padded by half for the slower days.
/// </summary>
public static class ModelSpeed
{
    /// <summary>Weights assumed for a local model that names no size (GB).</summary>
    public const double UnknownLocalGb = 5;

    /// <summary>The rates of <paramref name="model"/> on a computer whose graphics card has <paramref name="cardBytes"/> (0 = none or unknown).</summary>
    public static ModelSpeedRates For(CatalogModel model, long cardBytes)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!IsLocal(model))
        {
            return new ModelSpeedRates(2, 4_000, 70, false, false);
        }

        var gigabytes = model.MinVideoMemoryGb > 0 ? model.MinVideoMemoryGb : model.MemoryGb > 0 ? model.MemoryGb : UnknownLocalGb;
        var card = cardBytes / (1024.0 * 1024 * 1024);
        if (card >= gigabytes)
        {
            return new ModelSpeedRates(1, Math.Clamp(8_000 / gigabytes, 200, 6_000), Math.Clamp(350 / gigabytes, 5, 150), true, true);
        }

        return new ModelSpeedRates(2, Math.Clamp(400 / gigabytes, 20, 500), Math.Clamp(40 / gigabytes, 1, 30), true, false);
    }

    /// <summary>Whether <paramref name="model"/> runs on this computer or a server of the user (free; its speed is this computer's).</summary>
    public static bool IsLocal(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider is ModelProviders.Local or ModelProviders.AppOllama or ModelProviders.Endpoint;
    }

    /// <summary>The estimate of <paramref name="calls"/> calls of <paramref name="model"/> with the tokens of <paramref name="cost"/>.</summary>
    public static WorkEstimate Estimate(CatalogModel model, long cardBytes, int calls, CostEstimate cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        if (calls <= 0)
        {
            return WorkEstimate.None;
        }

        var rates = For(model, cardBytes);
        var fixedSeconds = (calls * rates.LatencySeconds) + (cost.InputTokens / rates.PromptTokensPerSecond);
        var low = fixedSeconds + (cost.OutputTokensMin / rates.OutputTokensPerSecond);
        var high = (fixedSeconds + (cost.OutputTokensMax / rates.OutputTokensPerSecond)) * 1.5;
        return new WorkEstimate(calls, cost, TimeSpan.FromSeconds(Math.Ceiling(low)), TimeSpan.FromSeconds(Math.Ceiling(Math.Max(low, high))), rates.Local);
    }
}

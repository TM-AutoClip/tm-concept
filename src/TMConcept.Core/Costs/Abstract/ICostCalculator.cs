using TMConcept.Core.Chat;
using TMConcept.Core.Models;

namespace TMConcept.Core.Costs;

/// <summary>Estimated (range, before a call) and exact (usage × price, after a call) costs.</summary>
public interface ICostCalculator
{
    /// <summary>Exact cost of a chat call from its usage (the provider's reported cost wins when there is one).</summary>
    decimal Exact(CatalogModel model, ChatUsage usage, decimal? reportedUsd = null);

    /// <summary>Estimate of chat calls: total prompt tokens, how many of them can come from the cache, and an output range.</summary>
    CostEstimate EstimateChat(CatalogModel model, long promptTokens, long cachedTokens, long outputMin, long outputMax);

    /// <summary>Tokens of a text in a language (estimate).</summary>
    long Tokens(string text, string language);
}

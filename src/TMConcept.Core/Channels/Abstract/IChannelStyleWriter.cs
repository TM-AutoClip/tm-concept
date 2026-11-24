using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;

namespace TMConcept.Core.Channels;

/// <summary>
/// The channel content of "Kanaldan esinlen" (0.1.53): ONE call to the chosen text model that describes the channel's style and subject area
/// from its titles, descriptions and numbers (<see cref="ChannelPromptBuilder"/>; it may not copy the channel's texts). Estimated before,
/// exact after; the call is a run of its own so the month's spending counts it.
/// </summary>
public interface IChannelStyleWriter
{
    /// <summary>The estimate of the call with <paramref name="model"/> (the largest listing; <paramref name="language"/> is the interface language).</summary>
    CostEstimate Estimate(CatalogModel model, string language);

    /// <summary>
    /// Writes the channel content of <paramref name="inspiration"/> in <paramref name="language"/> (the interface language). Throws
    /// <see cref="ChannelInspireException"/> ("answer") when the answer has no content, <see cref="Providers.ProviderException"/> when the call fails.
    /// </summary>
    Task<ChannelStyleResult> WriteAsync(ChannelInspiration inspiration, ResolvedModel model, string language, IProgress<ChatProgress>? progress,
        CancellationToken cancellationToken);
}

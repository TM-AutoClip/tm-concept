namespace TMConcept.Core.Styles;

/// <summary>One closing sentence of a language in both wordings.</summary>
/// <param name="Phrase">Which sentence.</param>
/// <param name="Subscribe">With "subscribe" (YouTube).</param>
/// <param name="Follow">With "follow" (Instagram, TikTok).</param>
public sealed record ClosingLine(ClosingPhrase Phrase, string Subscribe, string Follow);

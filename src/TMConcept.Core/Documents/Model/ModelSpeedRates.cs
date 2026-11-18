namespace TMConcept.Core.Documents;

/// <summary>
/// How fast a model is assumed to work, for a time estimate only (0.1.24): seconds before an answer starts, prompt tokens read per second,
/// answer tokens written per second; whether it runs on this computer and, if so, whether it is thought to fit on the graphics card.
/// </summary>
public sealed record ModelSpeedRates(double LatencySeconds, double PromptTokensPerSecond, double OutputTokensPerSecond, bool Local, bool OnGraphicsCard);

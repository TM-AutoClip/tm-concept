namespace TMConcept.Core.Channels;

/// <summary>
/// Where "Kanaldan esinlen" is: its step (<see cref="ChannelStages"/>), for a sample its number and how many there are (1-based; 0 otherwise),
/// and how far the step is (0–1).
/// </summary>
public sealed record ChannelProgress(string Stage, int Index, int Count, double Fraction);

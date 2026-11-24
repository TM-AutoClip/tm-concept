namespace TMConcept.Core.Channels;

/// <summary>The channel content the text model wrote (<see cref="IChannelStyleWriter.WriteAsync"/>), what the call cost and whether that cost is exact.</summary>
public sealed record ChannelStyleResult(string ConceptText, decimal CostUsd, bool Exact);

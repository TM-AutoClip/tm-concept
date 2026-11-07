namespace TMConcept.Core.Videos;

/// <summary>One line of a video's cost details: a step, what its calls cost together, whether all were exact, and how many calls.</summary>
public sealed record VideoCostLine(string Step, decimal Usd, bool Exact, int Count);

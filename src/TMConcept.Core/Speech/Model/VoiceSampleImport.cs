namespace TMConcept.Core.Speech;

/// <summary>The result of adding a voice sample: the sample, or why it was not added (<see cref="Problem"/>: "name", "file", "short", "read", "same").</summary>
public sealed record VoiceSampleImport(VoiceSampleInfo? Sample, string Problem);

namespace TMConcept.Core.References;

/// <summary>
/// The raw numbers of a reference (<see cref="ReferenceProfileBuilder.Build"/>): its grey frame numbers, its colour (null when it could not be
/// read) and its mono samples (null without a sound track) at <see cref="SampleRate"/>.
/// </summary>
public sealed record ReferenceMeasureInput(ReferenceFrameSeries Frames, ReferenceColors? Colors, float[]? Samples, int SampleRate);

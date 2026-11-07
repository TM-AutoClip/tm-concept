namespace TMConcept.Core.Markers;

/// <summary>A spoken part of an edited scene text: what is said (no marker in it), the voice speed factor and the silence after it.</summary>
public sealed record NarrationPart(string Text, double Speed, int PauseAfterMs);

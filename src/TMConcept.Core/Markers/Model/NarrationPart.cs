namespace TMConcept.Core.Markers;

/// <summary>A spoken part of an edited scene text: what is said (no marker in it), the voice speed factor and the silence after it.</summary>
/// <remarks>0.1.35: <see cref="Delivery"/> is the delivery marker's mood or sound for this part (empty = the voice's own).</remarks>
public sealed record NarrationPart(string Text, double Speed, int PauseAfterMs, string Delivery = "");

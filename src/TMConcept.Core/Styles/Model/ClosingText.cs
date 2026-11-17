namespace TMConcept.Core.Styles;

/// <summary>The closing of one video, resolved for its language and platform (<see cref="VideoStyleRules.Closing"/>).</summary>
/// <param name="Sentence">The sentence (spoken after the last scene's narration and/or shown on the end card).</param>
/// <param name="Label">End card button text ("Abone ol").</param>
/// <param name="Voice">The sentence is spoken.</param>
/// <param name="Screen">The end card is shown.</param>
public sealed record ClosingText(string Sentence, string Label, bool Voice, bool Screen);

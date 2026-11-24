using TMConcept.Core.Scenes;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;

namespace TMConcept.Core.References;

/// <summary>
/// What a reference suggests for a template (<see cref="ReferenceTempoMapper.Suggest"/>): the nearest narration tempo, the plan's targets
/// (changes every 10 s, camera share, ease family), the transition energy of that tempo and a palette made from its main colour (taken only
/// when the user agrees; null when it has no colour).
/// </summary>
public sealed record ReferenceSuggestion(NarrationTempo Tempo, TempoTarget Target, TransitionEnergy Energy, VideoPalette? Palette);

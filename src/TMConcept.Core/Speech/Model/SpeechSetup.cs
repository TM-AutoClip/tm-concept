using TMConcept.Core.Models;

namespace TMConcept.Core.Speech;

/// <summary>The narration of a profile: the engine, the catalog model, the resolved online model (null for a local voice), voice and speed.</summary>
public sealed record SpeechSetup(ISpeechEngine Engine, CatalogModel Model, ResolvedModel? Online, string Voice, double Speed);

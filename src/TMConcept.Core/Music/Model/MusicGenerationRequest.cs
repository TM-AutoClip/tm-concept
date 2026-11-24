using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// What to make (0.1.46, C5): the model with its key, the English prompt (<see cref="MusicPromptBuilder"/>), the length asked for (a model with
/// a fixed piece length ignores it), the tempo when the hints give one, whether it is without words, the folder the piece is written into and
/// a seed (the local model makes the same piece again with the same seed).
/// </summary>
/// <param name="Model">The model and its key.</param>
/// <param name="Prompt">The prompt.</param>
/// <param name="Seconds">The length asked for.</param>
/// <param name="Bpm">The tempo (null = the model chooses).</param>
/// <param name="Instrumental">Without words (the default: nothing sings over the narration).</param>
/// <param name="OutputFolder">An empty folder the piece is written into.</param>
/// <param name="Seed">The seed.</param>
public sealed record MusicGenerationRequest(ResolvedModel Model, string Prompt, int Seconds, int? Bpm, bool Instrumental, string OutputFolder, int Seed);

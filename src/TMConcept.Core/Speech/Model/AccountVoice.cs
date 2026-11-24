namespace TMConcept.Core.Speech;

/// <summary>
/// A voice of the user's account at an online provider (0.1.47, ElevenLabs: premade, cloned and library voices of "My Voices"): its id,
/// name, the provider's category ("premade", "cloned", "professional", "generated"…) and the languages the provider says it speaks
/// (two-letter codes; empty = the provider says nothing about its language).
/// </summary>
public sealed record AccountVoice(string Id, string Name, string Category, IReadOnlyList<string> Languages);

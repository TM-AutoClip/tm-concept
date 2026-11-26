namespace TMConcept.Core.Music;

/// <summary>What a piece is composed from (0.1.55, "Program composes"): the prompt, the length asked for, the tempo when one is given and the seed.</summary>
/// <param name="Prompt">The prompt (the music page's own words, Turkish or English, or the video's English prompt).</param>
/// <param name="Seconds">The length asked for.</param>
/// <param name="Bpm">The tempo (null = the mood's or the prompt's tempo word).</param>
/// <param name="Seed">The seed: the same brief with the same seed gives the same piece.</param>
public sealed record ScoreBrief(string Prompt, int Seconds, int? Bpm, int Seed);

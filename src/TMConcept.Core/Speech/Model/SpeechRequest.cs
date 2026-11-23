using TMConcept.Core.Models;

namespace TMConcept.Core.Speech;

/// <summary>
/// What to speak; <see cref="Online"/> is the resolved model and key of an online engine; <see cref="Emotion"/> the delivery of a marker
/// (0.1.35, <see cref="SpeechDeliveries"/>; empty = the voice's own), which only Gemini and ElevenLabs v3/v4 act on.
/// </summary>
public sealed record SpeechRequest(string Text, string Language, string Voice, double Speed, string OutputWav, ResolvedModel? Online = null, string Emotion = "");

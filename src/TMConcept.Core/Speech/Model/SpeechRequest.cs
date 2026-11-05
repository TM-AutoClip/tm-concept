using TMConcept.Core.Models;

namespace TMConcept.Core.Speech;

/// <summary>What to speak; <see cref="Online"/> is the resolved model and key of an online engine.</summary>
public sealed record SpeechRequest(string Text, string Language, string Voice, double Speed, string OutputWav, ResolvedModel? Online = null);

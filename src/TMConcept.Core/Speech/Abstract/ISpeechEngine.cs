using TMConcept.Core.Tools;

namespace TMConcept.Core.Speech;

/// <summary>
/// A voice engine: local (a model file on this computer, free) or online (a provider, paid; the key comes with the request). Engines come
/// from DI; a profile names one by <see cref="Code"/>. No voice cloning and no reference voice: only the engine's own voices.
/// </summary>
public interface ISpeechEngine
{
    /// <summary>Code ("supertonic", "piper-tr", or a provider code such as "openai").</summary>
    string Code { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>Runs on this computer.</summary>
    bool IsLocal { get; }

    /// <summary>Whether a content language is supported.</summary>
    bool Supports(string language);

    /// <summary>The engine's own voices.</summary>
    IReadOnlyList<SpeechVoice> Voices(string language);

    /// <summary>Whether the engine can speak now (a local model is installed).</summary>
    bool Ready { get; }

    /// <summary>Installs a local model (network; only after the user agreed). Online engines do nothing.</summary>
    Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Speaks <see cref="SpeechRequest.Text"/> into a WAV file.</summary>
    Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken);
}

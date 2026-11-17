using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Tests.Desktop;

/// <summary>A voice engine that speaks the given content languages (nothing is spoken).</summary>
internal sealed class LanguageVoice(IReadOnlyList<string> languages) : ISpeechEngine
{
    public string Code => "fake";

    public string Name => "Fake";

    public bool IsLocal => true;

    public bool Ready => true;

    public bool Supports(string language) => languages.Contains(language);

    public IReadOnlyList<SpeechVoice> Voices(string language) => [];

    public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
}

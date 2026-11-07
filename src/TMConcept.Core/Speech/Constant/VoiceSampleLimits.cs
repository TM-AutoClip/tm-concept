namespace TMConcept.Core.Speech;

/// <summary>
/// Limits of a voice sample. Pocket TTS takes at most 10 s of a reference by default (sherpa-onnx's max_reference_audio_len); its authors
/// suggest a clean recording of about 10 s, so a sample is cut to <see cref="MaxSeconds"/> and the voice is told to use all of it.
/// </summary>
public static class VoiceSampleLimits
{
    /// <summary>Shortest sample in seconds (a shorter one gives an unstable copy).</summary>
    public const int MinSeconds = 3;

    /// <summary>Longest sample in seconds (the rest of a longer recording is cut off).</summary>
    public const int MaxSeconds = 15;

    /// <summary>Sample rate the samples are kept in (Pocket TTS speaks at 24 kHz).</summary>
    public const int SampleRate = 24_000;

    /// <summary>Most characters of a sample's name.</summary>
    public const int NameMax = 40;

    /// <summary>Largest file that is read (a long recording of high quality is still far below this).</summary>
    public const long FileMaxBytes = 100L * 1024 * 1024;

    /// <summary>Files the import accepts (ffmpeg reads them all).</summary>
    public static readonly IReadOnlyList<string> Patterns = ["*.wav", "*.mp3", "*.m4a", "*.ogg", "*.flac"];

    /// <summary>The prefix of a voice id that names a sample ("sample:&lt;id&gt;").</summary>
    public const string VoicePrefix = "sample:";
}

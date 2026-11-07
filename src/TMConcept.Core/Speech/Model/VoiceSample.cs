namespace TMConcept.Core.Speech;

/// <summary>
/// A voice sample the user added for a voice that copies a voice (Pocket TTS): their own recording, turned into mono 16-bit 24 kHz WAV and kept
/// in the encrypted database (never as a file in the library). Cloning is the user's own responsibility (no consent step, rule 8).
/// </summary>
public sealed class VoiceSample
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The name the user gave it ("My voice").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The WAV file (mono, 16-bit, <see cref="VoiceSampleLimits.SampleRate"/> Hz, at most <see cref="VoiceSampleLimits.MaxSeconds"/> s).</summary>
    public byte[] Wav { get; set; } = [];

    /// <summary>Length in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>SHA-256 of <see cref="Wav"/> (hex), so the same recording is not added twice.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

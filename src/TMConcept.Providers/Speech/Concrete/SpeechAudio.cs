using TMConcept.Core.Speech;

namespace TMConcept.Providers.Speech;

/// <summary>
/// Audio of an online voice into the clip file (0.1.35): a WAV answer is written as it is, raw 16-bit little-endian mono PCM (Gemini's
/// older models, ElevenLabs' <c>pcm_24000</c>) gets a WAV header at the given rate. Anything else is refused.
/// </summary>
public static class SpeechAudio
{
    /// <summary>Whether <paramref name="audio"/> starts as a RIFF/WAVE file.</summary>
    public static bool IsWave(ReadOnlySpan<byte> audio) =>
        audio.Length > 12 && audio[..4].SequenceEqual("RIFF"u8) && audio.Slice(8, 4).SequenceEqual("WAVE"u8);

    /// <summary>Writes <paramref name="audio"/> (WAV, or raw PCM at <paramref name="pcmRate"/>) to <paramref name="path"/>.</summary>
    public static async Task WriteAsync(string path, byte[] audio, int pcmRate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (IsWave(audio))
        {
            await File.WriteAllBytesAsync(path, audio, cancellationToken).ConfigureAwait(false);
        }
        else if (audio.Length >= 2 && audio.Length % 2 == 0 && !LooksLikeText(audio))
        {
            var samples = new float[audio.Length / 2];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = BitConverter.ToInt16(audio, i * 2) / 32768f;
            }

            WaveFile.WritePcm16(path, samples, pcmRate);
        }
        else
        {
            throw new SpeechException("The provider did not return audio.") { Code = "engine" };
        }

        try
        {
            _ = WaveFile.DurationMs(path);
        }
        catch (InvalidDataException ex)
        {
            throw new SpeechException("The provider did not return a WAV file.", ex) { Code = "engine" };
        }
    }

    /// <summary>An error page or JSON where audio was expected (starts with '{', '[' or '&lt;').</summary>
    private static bool LooksLikeText(byte[] audio) => audio[0] is (byte)'{' or (byte)'[' or (byte)'<';
}

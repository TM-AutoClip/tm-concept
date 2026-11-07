namespace TMConcept.Core.Speech;

/// <summary>Turns a recording (WAV, MP3…) into a voice sample file: mono 16-bit <see cref="VoiceSampleLimits.SampleRate"/> Hz WAV, cut to a length.</summary>
public interface IVoiceSampleConverter
{
    /// <summary>Components the conversion needs (ffmpeg); installed after a confirmation when missing.</summary>
    IReadOnlyList<string> RequiredComponents { get; }

    /// <summary>Writes <paramref name="outputWav"/> from <paramref name="input"/>; false when the file could not be read as sound.</summary>
    Task<bool> ConvertAsync(string input, string outputWav, int maxSeconds, CancellationToken cancellationToken);
}

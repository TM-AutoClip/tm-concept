using System.Buffers.Binary;

namespace TMConcept.Core.Speech;

/// <summary>How the samples of a WAV file are laid out (<see cref="NarrationSilence"/>): rate, channels, bytes per sample, float or 16-bit.</summary>
/// <param name="Rate">Samples per second (per channel).</param>
/// <param name="Channels">Channels, interleaved.</param>
/// <param name="SampleBytes">Bytes of one sample of one channel.</param>
/// <param name="Float">32-bit float samples (else 16-bit PCM).</param>
public sealed record SampleLayout(int Rate, int Channels, int SampleBytes, bool Float)
{
    /// <summary>Bytes of one <see cref="NarrationGaps.WindowMs"/> window (whole frames, at least one).</summary>
    public int WindowBytes() => Math.Max(1, Rate * NarrationGaps.WindowMs / 1000) * Channels * SampleBytes;

    /// <summary>One sample in [-1, 1].</summary>
    public double Sample(ReadOnlySpan<byte> bytes) =>
        Float ? BinaryPrimitives.ReadSingleLittleEndian(bytes) : BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768.0;
}

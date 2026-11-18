using Shouldly;
using TMConcept.Core.Speech;

namespace TMConcept.Tests.Core;

/// <summary>
/// The time stretch of the voices without a speed of their own (Pocket TTS, Chatterbox, the VoxCPM voices): the length follows the speed and
/// the pitch stays (a plain resample would change both).
/// </summary>
public sealed class WaveTempoTests
{
    private const int Rate = 24_000;

    [Theory]
    [InlineData(1.5)]
    [InlineData(0.75)]
    [InlineData(1.25)]
    public void TheLengthFollowsTheSpeedAndThePitchStays(double speed)
    {
        var tone = Tone(220, Rate * 2);

        var stretched = WaveTempo.Stretch(tone, Rate, speed);

        stretched.Length.ShouldBe((int)Math.Round(tone.Length / speed));
        var middle = stretched.AsSpan(Rate / 10, stretched.Length - (Rate / 5));
        (Crossings(middle) * (double)Rate / middle.Length / 2).ShouldBe(220, 6, "the same tone, only shorter or longer");
        stretched.Max().ShouldBeLessThanOrEqualTo(1f);
    }

    [Fact]
    public void SpeedOneLeavesTheSamplesAsTheyAre()
    {
        var tone = Tone(220, Rate / 2);

        WaveTempo.Stretch(tone, Rate, 1.0).ShouldBe(tone);
    }

    [Fact]
    public void AFileIsStretchedInPlace()
    {
        var file = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "tempo-" + Guid.NewGuid().ToString("N") + ".wav");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            WaveFile.WritePcm16(file, Tone(220, Rate), Rate);

            var length = WaveTempo.Apply(file, 2.0);

            length.ShouldBe(500);
            WaveFile.DurationMs(file).ShouldBe(500);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static float[] Tone(double hertz, int samples)
    {
        var tone = new float[samples];
        for (var i = 0; i < samples; i++)
        {
            tone[i] = (float)(0.5 * Math.Sin(2 * Math.PI * hertz * i / Rate));
        }

        return tone;
    }

    private static int Crossings(ReadOnlySpan<float> samples)
    {
        var count = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] < 0) != (samples[i] < 0))
            {
                count++;
            }
        }

        return count;
    }
}

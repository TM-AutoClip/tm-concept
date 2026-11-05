using System.Diagnostics;
using Shouldly;
using TMConcept.Core.Playback;
using TMConcept.Desktop.Playback;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The real sound device of this system plays half a second of silence (nothing is heard): its clock must count only what was played,
/// start at zero and never pass what was written. Skipped when the system has no sound device.
/// </summary>
public sealed class AudioOutputTests
{
    [Fact]
    public async Task TheDeviceClockFollowsThePlayedSilence()
    {
        using IAudioOutput device = OperatingSystem.IsWindows() ? new WaveOutAudioOutput() : new PulseAudioOutput();
        try
        {
            device.Open(PlaybackFormat.SampleRate, PlaybackFormat.Channels);
        }
        catch (AudioOutputException ex)
        {
            Assert.Skip("No sound device: " + ex.Message);
        }

        device.PlayedBytes.ShouldBe(0);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        device.PlayedBytes.ShouldBe(0, "silence the device plays before the first write is not time");

        var chunk = new byte[(int)(PlaybackFormat.BytesPerMs * 50)];
        var watch = Stopwatch.StartNew();
        long written = 0;
        for (var i = 0; i < 10; i++)
        {
            device.Write(chunk, chunk.Length, TestContext.Current.CancellationToken);
            written += chunk.Length;
        }

        while (device.PlayedBytes < written && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            device.PlayedBytes.ShouldBeLessThanOrEqualTo(written);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        device.PlayedBytes.ShouldBe(written);
        watch.ElapsedMilliseconds.ShouldBeGreaterThan(400, "the device plays in real time");
    }
}

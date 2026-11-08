using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Playback;
using TMConcept.Engines.Playback;
using TMConcept.Engines.Processes;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The player engine with a real ffmpeg (from PATH or TMCONCEPT_TEST_FFMPEG; skipped without one) on a generated two-second clip, and a
/// simulated sound device that plays in real time. CPU only, no network.
/// </summary>
public sealed class PlaybackTests
{
    private const long ClipMs = 2_000;

    [Fact]
    public void ArgumentsSeekBothStreamsBeforeTheInputInInvariantSeconds()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            var video = PlaybackArguments.Video("in.mp4", 1_234, 404, 720, 30);
            var audio = PlaybackArguments.Audio("in.mp4", 1_234);
            foreach (var arguments in new[] { video, audio })
            {
                arguments.ToList().IndexOf("-ss").ShouldBeLessThan(arguments.ToList().IndexOf("-i"));
                arguments[arguments.ToList().IndexOf("-ss") + 1].ShouldBe("1.234");
            }

            video[video.ToList().IndexOf("-vf") + 1].ShouldBe("scale=404:720:force_original_aspect_ratio=decrease,pad=404:720:(ow-iw)/2:(oh-ih)/2,fps=30");
            video.ShouldContain("bgra");
            audio.ShouldContain("s16le");
            audio[audio.ToList().IndexOf("-ar") + 1].ShouldBe("48000");
            PlaybackArguments.Frame("in.mp4", 500, 404, 720).ShouldContain("-frames:v");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task PlaysToTheEndWithTheFramesFollowingTheSound()
    {
        var (ffmpeg, clip) = await ClipAsync();
        var device = new RealTimeDevice();
        using var playback = new FfmpegPlayback(new Devices(device), ChildProcessTests.Guard(), TimeProvider.System, NullLogger<FfmpegPlayback>.Instance);
        var frames = new List<(long Frame, double Clock)>();
        playback.FrameReady += () =>
        {
            var copy = new byte[64 * 36 * 4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(copy, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                playback.CopyLatest(handle.AddrOfPinnedObject(), 64 * 4, out var at);
                lock (frames)
                {
                    frames.Add((at, playback.PositionMs));
                }
            }
            finally
            {
                handle.Free();
            }
        };
        var stopped = new TaskCompletionSource<PlaybackStop>(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.Stopped += s => stopped.TrySetResult(s);
        playback.Open(new PlaybackSource(ffmpeg, clip, 64, 36, 30, ClipMs));

        playback.Play(0);
        var stop = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        stop.Ended.ShouldBeTrue(stop.Error);
        playback.HasSound.ShouldBeTrue();
        playback.IsPlaying.ShouldBeFalse();
        playback.PositionMs.ShouldBe(ClipMs);
        device.Written.ShouldBeInRange(Bytes(ClipMs * 0.9), Bytes(ClipMs * 1.1));
        lock (frames)
        {
            frames.Count.ShouldBeGreaterThan(30, "most of the 60 frames are shown in real time");
            frames.Select(f => f.Frame).ShouldBeInOrder();

            // Sync: when a frame is handed over, the sound clock is at that frame's time (at most a few frames off, also on a loaded machine).
            var offsets = frames.Skip(1).Select(f => Math.Abs(f.Frame - f.Clock)).Order().ToList();
            offsets[offsets.Count / 2].ShouldBeLessThan(70, "median picture–sound offset (ms)");
        }
    }

    [Fact]
    public async Task PauseStopsTheClockAndBothStreamsAndPlayGoesOnFromThere()
    {
        var (ffmpeg, clip) = await ClipAsync();
        var device = new RealTimeDevice();
        using var playback = new FfmpegPlayback(new Devices(device), ChildProcessTests.Guard(), TimeProvider.System, NullLogger<FfmpegPlayback>.Instance);
        var frames = 0;
        playback.FrameReady += () => Interlocked.Increment(ref frames);
        playback.Open(new PlaybackSource(ffmpeg, clip, 64, 36, 30, ClipMs));

        playback.Play(0);
        await Until(() => playback.PositionMs >= 600);
        var paused = playback.Pause();
        var shown = Volatile.Read(ref frames);
        await Task.Delay(400, TestContext.Current.CancellationToken);

        paused.ShouldBeInRange(550, 1_200);
        playback.IsPlaying.ShouldBeFalse();
        playback.PositionMs.ShouldBe(paused);
        Volatile.Read(ref frames).ShouldBeLessThanOrEqualTo(shown + 1, "no picture after the pause");

        // Play continues at the paused position: the new sound starts there (both streams seek to the same instant).
        var second = new RealTimeDevice();
        device.Next = second;
        playback.Play(paused);
        await Until(() => playback.PositionMs >= paused + 300);
        playback.PositionMs.ShouldBeGreaterThanOrEqualTo(paused + 300);
        playback.Stop();
        second.FirstWriteAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ASeekWhilePausedShowsThatPictureAndASeekWhilePlayingStartsTheSoundThere()
    {
        var (ffmpeg, clip) = await ClipAsync();
        var device = new RealTimeDevice();
        using var playback = new FfmpegPlayback(new Devices(device), ChildProcessTests.Guard(), TimeProvider.System, NullLogger<FfmpegPlayback>.Instance);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.FrameReady += () => ready.TrySetResult();
        playback.Open(new PlaybackSource(ffmpeg, clip, 64, 36, 30, ClipMs));

        playback.ShowFrame(1_500);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var buffer = new byte[64 * 36 * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            playback.CopyLatest(handle.AddrOfPinnedObject(), 64 * 4, out var at).ShouldBeTrue();
            at.ShouldBe(1_500);
        }
        finally
        {
            handle.Free();
        }

        playback.PositionMs.ShouldBe(1_500);
        playback.IsPlaying.ShouldBeFalse();

        var stopped = new TaskCompletionSource<PlaybackStop>(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.Stopped += s => stopped.TrySetResult(s);
        playback.Play(1_000);
        (await stopped.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).Ended.ShouldBeTrue();

        // From 1.0 s of a 2.0 s clip: about one second of sound.
        device.Written.ShouldBeInRange(Bytes(900), Bytes(1_150));
    }

    [Fact]
    public async Task WithoutASoundDeviceTheVideoStillPlaysOnTheWallClock()
    {
        var (ffmpeg, clip) = await ClipAsync();
        using var playback = new FfmpegPlayback(new NoDevice(), ChildProcessTests.Guard(), TimeProvider.System, NullLogger<FfmpegPlayback>.Instance);
        var stopped = new TaskCompletionSource<PlaybackStop>(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.Stopped += s => stopped.TrySetResult(s);
        playback.Open(new PlaybackSource(ffmpeg, clip, 64, 36, 30, ClipMs));
        var watch = Stopwatch.StartNew();

        playback.Play(0);
        (await stopped.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).Ended.ShouldBeTrue();

        playback.HasSound.ShouldBeFalse();
        watch.ElapsedMilliseconds.ShouldBeGreaterThan(1_500, "real time, not as fast as ffmpeg can decode");
    }

    private static long Bytes(double milliseconds) => (long)(milliseconds * PlaybackFormat.BytesPerMs);

    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue();
    }

    /// <summary>ffmpeg and a 2 s clip (64×36 test picture, 440 Hz tone) made once per test run.</summary>
    private static async Task<(string Ffmpeg, string Clip)> ClipAsync()
    {
        var ffmpeg = Ffmpeg();
        if (ffmpeg is null)
        {
            Assert.Skip("ffmpeg was not found (PATH or TMCONCEPT_TEST_FFMPEG).");
        }

        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "playback");
        Directory.CreateDirectory(folder);
        var clip = Path.Combine(folder, "clip-2s.mp4");
        if (File.Exists(clip))
        {
            return (ffmpeg, clip);
        }

        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".mp4");
        var info = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=64x36:rate=30:duration=2", "-f", "lavfi", "-i",
            "sine=frequency=440:sample_rate=48000:duration=2", "-c:v", "mpeg4", "-q:v", "5", "-c:a", "aac", "-shortest", temporary,
        })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.ShouldBe(0, error);
        File.Move(temporary, clip, overwrite: true);
        return (ffmpeg, clip);
    }

    /// <summary>ffmpeg from TMCONCEPT_TEST_FFMPEG or PATH; null without one (the real-ffmpeg tests are skipped).</summary>
    internal static string? Ffmpeg()
    {
        var configured = Environment.GetEnvironmentVariable("TMCONCEPT_TEST_FFMPEG");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
        {
            return configured;
        }

        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
    }

    /// <summary>Hands out the current simulated device (a new one after <see cref="RealTimeDevice.Next"/> is set).</summary>
    private sealed class Devices(RealTimeDevice first) : IAudioOutputFactory
    {
        private RealTimeDevice _current = first;
        private bool _given;

        public IAudioOutput Create()
        {
            if (_given && _current.Next is { } next)
            {
                _current = next;
            }

            _given = true;
            return _current;
        }
    }

    private sealed class NoDevice : IAudioOutputFactory
    {
        public IAudioOutput Create() => new Missing();

        private sealed class Missing : IAudioOutput
        {
            public long PlayedBytes => 0;

            public void Open(int sampleRate, int channels) => throw new AudioOutputException("no device in this test");

            public void Write(byte[] buffer, int count, CancellationToken cancellationToken) => throw new InvalidOperationException();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>A device that plays in real time from its first write and holds at most 250 ms (like the real ones).</summary>
    private sealed class RealTimeDevice : IAudioOutput
    {
        private readonly Lock _lock = new();
        private long _written;
        private long _start;

        public RealTimeDevice? Next { get; set; }

        public long? FirstWriteAt { get; private set; }

        public long Written
        {
            get
            {
                lock (_lock)
                {
                    return _written;
                }
            }
        }

        public long PlayedBytes
        {
            get
            {
                lock (_lock)
                {
                    return _written == 0 ? 0 : Math.Min(_written, (long)(Stopwatch.GetElapsedTime(_start).TotalMilliseconds * PlaybackFormat.BytesPerMs));
                }
            }
        }

        public void Open(int sampleRate, int channels)
        {
            sampleRate.ShouldBe(PlaybackFormat.SampleRate);
            channels.ShouldBe(PlaybackFormat.Channels);
        }

        public void Write(byte[] buffer, int count, CancellationToken cancellationToken)
        {
            (count % 4).ShouldBe(0, "only whole stereo samples");
            while (Written - PlayedBytes > Bytes(250))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(5);
            }

            lock (_lock)
            {
                if (_written == 0)
                {
                    _start = Stopwatch.GetTimestamp();
                    FirstWriteAt = _start;
                }

                _written += count;
            }
        }

        public void Dispose()
        {
        }
    }
}

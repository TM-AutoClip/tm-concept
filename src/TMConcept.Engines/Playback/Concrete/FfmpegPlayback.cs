using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Playback;
using TMConcept.Engines.Processes;

namespace TMConcept.Engines.Playback;

/// <summary>
/// The in-app video player's engine, without any interface code. Two ffmpeg processes per play: picture (BGRA frames of a fixed size)
/// and sound (16-bit PCM written to the system's sound device). The sound device is the clock (<see cref="PlaybackClock"/>); every frame
/// is shown when its time comes, waits when early and is dropped when late (<see cref="FrameScheduler"/>). Pause stops both processes and
/// remembers the position; play and seek start both again at the position, so the two streams always begin together (no drift can build
/// up across a pause). A paused seek decodes a single picture. Without a sound device the video plays silently on the wall clock (logged).
/// The newest frame is kept in one buffer; <see cref="FrameReady"/> says it changed and the interface copies it (no frame allocations).
/// Events come from background threads. The ffmpeg processes are handed to the <see cref="ChildProcessGuardBase"/> (they end with the app).
/// </summary>
public sealed class FfmpegPlayback(IAudioOutputFactory audio, ChildProcessGuardBase children, TimeProvider time, ILogger<FfmpegPlayback> logger)
    : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Lock _frameLock = new();
    private readonly PlaybackClock _clock = new(time);
    private PlaybackSource? _source;
    private Session? _session;
    private int _generation;
    private byte[]? _latest;
    private long _latestMs;
    private double _volume = 1;

    /// <summary>A new frame is ready (<see cref="CopyLatest"/>); background thread.</summary>
    public event Action? FrameReady;

    /// <summary>Playing stopped by itself (end or error); background thread. Not raised for <see cref="Pause"/> or <see cref="Stop"/>.</summary>
    public event Action<PlaybackStop>? Stopped;

    /// <summary>Whether it plays.</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _session is not null;
            }
        }
    }

    /// <summary>Whether the last play has sound on a device (false: no device, or the video has no sound).</summary>
    public bool HasSound { get; private set; }

    /// <summary>Current position (ms).</summary>
    public long PositionMs => (long)Math.Round(Math.Clamp(_clock.PositionMs, 0, _source?.DurationMs ?? 0));

    /// <summary>Volume 0–1 (applied to the samples, takes effect after the device buffer, about 0.25 s).</summary>
    public double Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, Math.Clamp(value, 0, 1));
    }

    /// <summary>Takes a new video (stops the old one; nothing plays until <see cref="Play"/>).</summary>
    public void Open(PlaybackSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Stop();
        lock (_frameLock)
        {
            _latest = null;
        }

        _source = source;
        _clock.Start(0, null);
        _clock.Freeze();
    }

    /// <summary>Plays from <paramref name="fromMs"/>.</summary>
    public void Play(long fromMs)
    {
        var source = _source ?? throw new InvalidOperationException("No video is open.");
        fromMs = Math.Clamp(fromMs, 0, Math.Max(0, source.DurationMs - 1));
        lock (_gate)
        {
            StopLocked();
            var generation = ++_generation;
            var session = new Session(generation);
            _session = session;
            IAudioOutput? output = null;
            try
            {
                output = audio.Create();
                output.Open(PlaybackFormat.SampleRate, PlaybackFormat.Channels);
                session.Output = output;
                session.Audio = Start(source.Ffmpeg, PlaybackArguments.Audio(source.File, fromMs), session);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                logger.LogError(ex, "ffmpeg could not be started for the sound");
                output?.Dispose();
                session.Output = null;
                session.Audio = null;
            }
            catch (AudioOutputException ex)
            {
                logger.LogWarning(ex, "No sound device; the video plays without sound");
                output?.Dispose();
                session.Output = null;
            }

            HasSound = session.Output is not null;
            Process picture;
            try
            {
                picture = Start(source.Ffmpeg, PlaybackArguments.Video(source.File, fromMs, source.Width, source.Height, source.Fps), session);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                logger.LogError(ex, "ffmpeg could not be started for playback");
                StopLocked();
                throw;
            }

            session.Video = picture;
            var device = session.Output;
            _clock.Start(fromMs, device is null ? null : () => device.PlayedBytes);
            if (session.Audio is { } sound && device is not null)
            {
                _ = Task.Factory.StartNew(() => SoundLoop(session, sound, device), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }

            _ = Task.Factory.StartNew(() => PictureLoop(session, picture, source, fromMs), CancellationToken.None, TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            logger.LogInformation("Playing from {Position} ms (sound: {Sound})", fromMs, device is not null);
        }
    }

    /// <summary>Pauses: stops both streams and returns the position (play again continues from it).</summary>
    public long Pause()
    {
        lock (_gate)
        {
            var position = _clock.Freeze();
            StopLocked();
            return Math.Clamp(position, 0, _source?.DurationMs ?? 0);
        }
    }

    /// <summary>Stops (the position stays where it was).</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _clock.Freeze();
            StopLocked();
        }
    }

    /// <summary>Decodes the single picture at <paramref name="atMs"/> (paused seek, first picture); the clock moves there.</summary>
    public void ShowFrame(long atMs)
    {
        var source = _source ?? throw new InvalidOperationException("No video is open.");
        atMs = Math.Clamp(atMs, 0, Math.Max(0, source.DurationMs - 1));
        int generation;
        lock (_gate)
        {
            StopLocked();
            generation = ++_generation;
            _clock.Start(atMs, null);
            _clock.Freeze();
        }

        _ = Task.Run(() => SingleFrame(source, atMs, generation));
    }

    /// <summary>Copies the newest frame into a bitmap's memory (row by row when its stride differs); false before the first frame.</summary>
    public bool CopyLatest(IntPtr destination, int rowBytes, out long positionMs)
    {
        lock (_frameLock)
        {
            positionMs = _latestMs;
            if (_latest is null || _source is not { } source)
            {
                return false;
            }

            var row = source.Width * 4;
            if (rowBytes == row)
            {
                Marshal.Copy(_latest, 0, destination, _latest.Length);
            }
            else
            {
                for (var y = 0; y < source.Height; y++)
                {
                    Marshal.Copy(_latest, y * row, destination + (y * rowBytes), row);
                }
            }

            return true;
        }
    }

    /// <summary>Whether a frame is waiting to be copied (false after <see cref="ReleaseFrame"/> until a new one is decoded).</summary>
    public bool HasFrame
    {
        get
        {
            lock (_frameLock)
            {
                return _latest is not null;
            }
        }
    }

    /// <summary>Drops the newest-frame buffer (the player was closed); the next play or paused seek makes it again.</summary>
    public void ReleaseFrame()
    {
        lock (_frameLock)
        {
            _latest = null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }

    /// <summary>Scales 16-bit samples in place (1 = untouched).</summary>
    private static void ApplyVolume(byte[] buffer, int count, double volume)
    {
        if (volume >= 0.999)
        {
            return;
        }

        var samples = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, count));
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)Math.Clamp(samples[i] * volume, short.MinValue, short.MaxValue);
        }
    }

    private Process Start(string ffmpeg, IReadOnlyList<string> arguments, Session session)
    {
        var info = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        ProcessRunner.Clean(info.Environment);
        var process = new Process { StartInfo = info };
        process.ErrorDataReceived += (_, e) => session.Error(e.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("ffmpeg could not be started.");
        }

        session.Processes.Add(process);
        session.Adoptions.Add(children.Adopt(process));
        process.BeginErrorReadLine();
        return process;
    }

    private void SoundLoop(Session session, Process sound, IAudioOutput device)
    {
        var buffer = new byte[(int)(PlaybackFormat.BytesPerMs * 40)];
        var carry = 0;
        long total = 0;
        try
        {
            var stream = sound.StandardOutput.BaseStream;
            while (!session.Cancel.IsCancellationRequested)
            {
                var n = stream.Read(buffer, carry, buffer.Length - carry);
                if (n <= 0)
                {
                    break;
                }

                // Only whole sample frames go to the device (4 bytes: two 16-bit channels).
                var available = carry + n;
                var whole = available - (available % 4);
                ApplyVolume(buffer, whole, Volume);
                device.Write(buffer, whole, session.Cancel.Token);
                total += whole;
                carry = available - whole;
                if (carry > 0)
                {
                    Array.Copy(buffer, whole, buffer, 0, carry);
                }
            }
        }
        catch (AudioOutputException ex)
        {
            logger.LogWarning(ex, "The sound device failed during playback; the picture goes on");
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Stopped (pause, seek, end) kills the process under the read: expected. Without a stop it is a real failure.
            if (!session.Cancel.IsCancellationRequested)
            {
                logger.LogError(ex, "Reading the sound failed; the picture goes on");
            }
        }
        finally
        {
            if (!session.Cancel.IsCancellationRequested)
            {
                _clock.SoundEnded(total);
            }
        }
    }

    private void PictureLoop(Session session, Process picture, PlaybackSource source, long startMs)
    {
        var buffer = new byte[source.Width * source.Height * 4];
        var frameLength = 1000.0 / source.Fps;
        long index = 0;
        var shown = false;
        try
        {
            var stream = picture.StandardOutput.BaseStream;
            while (!session.Cancel.IsCancellationRequested)
            {
                if (!ReadExactly(stream, buffer))
                {
                    picture.WaitForExit(2000);
                    var failed = !shown && picture.HasExited && picture.ExitCode != 0;
                    Finish(session, failed ? session.LastError() : string.Empty);
                    return;
                }

                var at = FrameScheduler.FrameTime(startMs, index++, source.Fps);
                var decision = FrameScheduler.Decide(at, _clock.PositionMs, frameLength);
                var waited = Stopwatch.StartNew();
                while (decision == FrameDecision.Wait && waited.ElapsedMilliseconds < PlaybackFormat.MaxWaitMs && !session.Cancel.IsCancellationRequested)
                {
                    Thread.Sleep(Math.Clamp((int)(at - _clock.PositionMs), 1, 10));
                    decision = FrameScheduler.Decide(at, _clock.PositionMs, frameLength);
                }

                if (session.Cancel.IsCancellationRequested)
                {
                    return;
                }

                // The first frame is always shown (a frozen start would look broken); later late frames are skipped.
                if (decision == FrameDecision.Drop && shown)
                {
                    continue;
                }

                Publish(buffer, (long)Math.Round(at), session.Generation);
                shown = true;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (!session.Cancel.IsCancellationRequested)
            {
                logger.LogError(ex, "Reading the video frames failed");
                Finish(session, ex.Message);
            }
        }
    }

    private void SingleFrame(PlaybackSource source, long atMs, int generation)
    {
        var session = new Session(generation);
        try
        {
            using var process = Start(source.Ffmpeg, PlaybackArguments.Frame(source.File, atMs, source.Width, source.Height), session);
            var buffer = new byte[source.Width * source.Height * 4];
            if (ReadExactly(process.StandardOutput.BaseStream, buffer))
            {
                Publish(buffer, atMs, generation);
            }
            else
            {
                process.WaitForExit(2000);
                if (process.HasExited && process.ExitCode != 0)
                {
                    logger.LogWarning("A paused picture could not be decoded: {Error}", session.LastError());
                }
            }

            session.Kill();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "A paused picture could not be decoded");
        }
    }

    private void Publish(byte[] frame, long positionMs, int generation)
    {
        lock (_frameLock)
        {
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }

            _latest ??= new byte[frame.Length];
            if (_latest.Length != frame.Length)
            {
                _latest = new byte[frame.Length];
            }

            Buffer.BlockCopy(frame, 0, _latest, 0, frame.Length);
            _latestMs = positionMs;
        }

        FrameReady?.Invoke();
    }

    private void Finish(Session session, string error)
    {
        long position;
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            position = error.Length == 0 ? _source?.DurationMs ?? 0 : _clock.Freeze();
            _clock.Start(position, null);
            _clock.Freeze();
            StopLocked();
        }

        if (error.Length > 0)
        {
            logger.LogError("Playback stopped with an error: {Error}", error);
        }

        Stopped?.Invoke(new PlaybackStop(error.Length == 0, error, position));
    }

    private void StopLocked()
    {
        Interlocked.Increment(ref _generation);
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        session.Cancel.Cancel();
        session.Kill();
        session.Output?.Dispose();
    }

    /// <summary>One play: its processes, the sound device and the last error lines of ffmpeg.</summary>
    private sealed class Session(int generation)
    {
        private readonly Lock _lock = new();
        private readonly Queue<string> _errors = new();

        public int Generation { get; } = generation;

        public CancellationTokenSource Cancel { get; } = new();

        public List<Process> Processes { get; } = [];

        public List<IDisposable> Adoptions { get; } = [];

        public Process? Video { get; set; }

        public Process? Audio { get; set; }

        public IAudioOutput? Output { get; set; }

        public void Error(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (_lock)
            {
                _errors.Enqueue(line.Trim());
                while (_errors.Count > 5)
                {
                    _errors.Dequeue();
                }
            }
        }

        public string LastError()
        {
            lock (_lock)
            {
                return _errors.Count == 0 ? "ffmpeg could not read the video." : string.Join(" | ", _errors);
            }
        }

        public void Kill()
        {
            foreach (var process in Processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }

                process.Dispose();
            }

            foreach (var adoption in Adoptions)
            {
                adoption.Dispose();
            }

            Processes.Clear();
            Adoptions.Clear();
        }
    }
}

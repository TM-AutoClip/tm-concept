using NAudio.Wave;
using TMConcept.Core.Playback;

namespace TMConcept.Desktop.Playback;

/// <summary>
/// Windows sound device: NAudio's WaveOut (winmm) fed from a small buffer. The device never stops; while no sound is queued it plays
/// silence, which <see cref="AudioContentClock"/> leaves out of the played bytes. Writes block while more than a quarter second is queued
/// (that is the player's back-pressure and keeps a volume change quick).
/// </summary>
public sealed class WaveOutAudioOutput : IAudioOutput
{
    private static readonly TimeSpan Queue = TimeSpan.FromMilliseconds(250);
    private readonly AudioContentClock _content = new();
    private WaveOutEvent? _device;
    private BufferedWaveProvider? _buffer;

    /// <inheritdoc />
    public long PlayedBytes
    {
        get
        {
            try
            {
                return _device is { } device ? _content.ContentBytes(device.GetPosition()) : 0;
            }
            catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException or NullReferenceException)
            {
                // The device was closed under the call (stop); the clock is not read after that.
                return 0;
            }
        }
    }

    /// <inheritdoc />
    public void Open(int sampleRate, int channels)
    {
        try
        {
            _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, channels))
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = false,
                ReadFully = false,
            };
            _device = new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3 };
            _device.Init(new Padded(_buffer, _content));
            _device.Play();
        }
        catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException)
        {
            Dispose();
            throw new AudioOutputException("The Windows sound device could not be opened: " + ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public void Write(byte[] buffer, int count, CancellationToken cancellationToken)
    {
        var queue = _buffer ?? throw new InvalidOperationException("The device is not open.");
        while (queue.BufferedDuration > Queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(10);
        }

        queue.AddSamples(buffer, 0, count);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _device?.Stop();
            _device?.Dispose();
        }
        catch (NAudio.MmException)
        {
            // The device is gone already.
        }

        _device = null;
        _buffer = null;
    }

    /// <summary>Gives the device whole sample frames from the buffer and fills the rest with silence, recording both for the clock.</summary>
    private sealed class Padded(BufferedWaveProvider buffer, AudioContentClock content) : IWaveProvider
    {
        public WaveFormat WaveFormat => buffer.WaveFormat;

        public int Read(byte[] target, int offset, int count)
        {
            var block = WaveFormat.BlockAlign;
            var ready = buffer.BufferedBytes;
            var want = Math.Min(count, ready - (ready % block));
            var read = want > 0 ? buffer.Read(target, offset, want) : 0;
            if (read < count)
            {
                Array.Clear(target, offset + read, count - read);
            }

            content.Given(read, count - read);
            return count;
        }
    }
}

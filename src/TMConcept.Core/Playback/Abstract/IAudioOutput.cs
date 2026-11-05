namespace TMConcept.Core.Playback;

/// <summary>
/// The sound device of the in-app player (Windows: WaveOut; Linux: PulseAudio or PipeWire's PulseAudio server). The player decodes the
/// sound with ffmpeg into 16-bit PCM and writes it here; <see cref="PlayedBytes"/> is the master clock of the picture, so it must count
/// only the sound that was really heard (not the silence the device plays before the first bytes arrive).
/// </summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>Opens the device for 16-bit little-endian PCM; <see cref="AudioOutputException"/> when there is no usable device.</summary>
    void Open(int sampleRate, int channels);

    /// <summary>Queues samples; blocks while the device buffer is full (that keeps the decoder in step with the sound).</summary>
    void Write(byte[] buffer, int count, CancellationToken cancellationToken);

    /// <summary>Bytes of the written sound that have been played so far.</summary>
    long PlayedBytes { get; }
}

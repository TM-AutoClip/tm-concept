using TMConcept.Core.Playback;

namespace TMConcept.Desktop.Playback;

/// <summary>Creates the sound device <typeparamref name="T"/> chosen by the OS module.</summary>
public sealed class AudioOutputFactory<T> : IAudioOutputFactory
    where T : IAudioOutput, new()
{
    /// <inheritdoc />
    public IAudioOutput Create() => new T();
}

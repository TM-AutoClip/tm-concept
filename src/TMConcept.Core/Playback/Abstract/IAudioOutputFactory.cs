namespace TMConcept.Core.Playback;

/// <summary>Creates the sound device of this operating system (registered by the OS module; no "if Windows" in the player).</summary>
public interface IAudioOutputFactory
{
    /// <summary>A new, closed device.</summary>
    IAudioOutput Create();
}

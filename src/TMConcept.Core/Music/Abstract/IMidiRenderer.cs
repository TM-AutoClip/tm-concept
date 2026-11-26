namespace TMConcept.Core.Music;

/// <summary>
/// Plays a MIDI file into a WAV file with a SoundFont (0.1.55, "Program composes"; on the processor, no graphics card, no model). Its parts are
/// components installed after the user's yes (rule 18); a failure throws <see cref="MusicGenerationException"/>.
/// </summary>
public interface IMidiRenderer
{
    /// <summary>Whether the synthesizer and its SoundFont are installed (files only).</summary>
    bool Ready { get; }

    /// <summary>Plays <paramref name="midiFile"/> into <paramref name="wavFile"/> (stereo, 44.1 kHz, with the instruments' fading tail).</summary>
    Task RenderAsync(string midiFile, string wavFile, CancellationToken cancellationToken);
}

namespace TMConcept.Core.Styles;

/// <summary>
/// Where a template's background music comes from (0.1.44, C3; <see cref="VideoMusic.Source"/>). Stored as a number. Only
/// <see cref="Off"/>, <see cref="OwnFile"/>, (0.1.45) <see cref="Library"/> and (0.1.46, C5) generated music; the editor shows them in
/// <see cref="MusicSources.Available"/>'s order.
/// </summary>
public enum MusicSource
{
    /// <summary>No music (the default; the video sounds as before 0.1.44).</summary>
    Off = 0,

    /// <summary>A track of the app's ready library (C4, 0.1.45): chosen by the concept, picked by the user, or picked from an Openverse search.</summary>
    Library = 1,

    /// <summary>Music made for the concept by the local model (0.1.46, C5: ACE-Step 1.5 through rakkase; NVIDIA only).</summary>
    GenerateLocal = 2,

    /// <summary>Music made for the concept by an online model (0.1.46, C5: Google Lyria, ElevenLabs Music).</summary>
    GenerateOnline = 3,

    /// <summary>A file the user chose ("Kendi dosyam"); its licence is the user's responsibility.</summary>
    OwnFile = 4,
}

namespace TMConcept.Engines.Music;

/// <summary>The numbers of the composed music's synthesizer (0.1.55, "Program composes").</summary>
public static class MusicSynthRules
{
    /// <summary>The name of its npm package folder (<c>&lt;tools&gt;/engines/music-synth</c>) and of its embedded files.</summary>
    public const string PackageName = "music-synth";

    /// <summary>The script that plays a MIDI file into a WAV file (inside the package folder).</summary>
    public const string ScriptName = "render.mjs";

    /// <summary>The file that shows the package is installed (inside the package folder).</summary>
    public const string InstalledFile = "node_modules/spessasynth_core/package.json";

    /// <summary>The folder of the SoundFonts (inside the tools folder).</summary>
    public const string SoundFontFolder = "soundfonts";

    /// <summary>The sample rate of the piece.</summary>
    public const int SampleRate = 44_100;

    /// <summary>The seconds of fading tail after the last note.</summary>
    public const string TailSeconds = "2.5";

    /// <summary>
    /// About what <c>npm ci</c> downloads: the tarballs of spessasynth_core 4.4.10, stb-vorbis 0.0.7 and fflate 0.8.3 (npm gives no size before
    /// the install; measured on 26 Nov 2026).
    /// </summary>
    public const long PackageBytes = 1_400_000;
}

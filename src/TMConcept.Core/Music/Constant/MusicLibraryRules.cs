namespace TMConcept.Core.Music;

/// <summary>
/// The fixed numbers of the music library (0.1.45, C4 + F7 + F8). The beat search follows ReelMimic's <c>beat_grid.py</c> (MIT): spectral
/// flux of 2048-sample frames every 128 samples at 22 050 Hz, a 64-frame moving mean taken off, the tempo between 70 and 170 beats per
/// minute by autocorrelation, the phase whose comb of beats collects the most onset energy. The entry point follows the idea of its
/// <c>analyze.py</c> (MIT): an opening is compared with the 5-second parts after it.
/// </summary>
public static class MusicLibraryRules
{
    /// <summary>The id start of a library track.</summary>
    public const string LibraryPrefix = "incompetech:";

    /// <summary>The id start of a track picked from an Openverse search.</summary>
    public const string OpenversePrefix = "openverse:";

    /// <summary>The catalog format this app reads.</summary>
    public const int CatalogSchema = 1;

    /// <summary>The catalog's embedded resource name.</summary>
    public const string CatalogResource = "TMConcept.Catalog.music.json";

    /// <summary>The library's folder under the tools folder.</summary>
    public const string Folder = "music-library";

    /// <summary>The file written after a complete install.</summary>
    public const string Marker = ".installed";

    /// <summary>The only host the library's files come from.</summary>
    public const string Host = "incompetech.com";

    /// <summary>The licence of every library track.</summary>
    public const string License = "CC BY 4.0";

    /// <summary>The sample rate a track is read at for its analysis (mono).</summary>
    public const int SampleRate = 22050;

    /// <summary>The longest part of a track read for its analysis (seconds).</summary>
    public const int MaxAnalysisSeconds = 600;

    /// <summary>The frame of the spectral flux (samples).</summary>
    public const int FluxWindow = 2048;

    /// <summary>The step between two flux frames (samples).</summary>
    public const int FluxHop = 128;

    /// <summary>The moving mean taken off the flux (frames).</summary>
    public const int FluxMean = 64;

    /// <summary>The slowest tempo searched.</summary>
    public const double MinBpm = 70;

    /// <summary>The fastest tempo searched.</summary>
    public const double MaxBpm = 170;

    /// <summary>The step of the tempo search.</summary>
    public const double BpmStep = 0.05;

    /// <summary>The step of the phase search (seconds).</summary>
    public const double PhaseStep = 0.005;

    /// <summary>How far around the autocorrelation's tempo the comb refines it (a share of it).</summary>
    public const double RefineShare = 0.015;

    /// <summary>The step of the refined tempo (beats per minute).</summary>
    public const double RefineStep = 0.01;

    /// <summary>Half the width of the smoothing of the phase search (seconds).</summary>
    public const double PhaseSmoothSeconds = 0.03;

    /// <summary>
    /// Where a sound is heard after the start of the envelope step that sees it rise (samples): the step's frame starts one hop later and the
    /// sound rises most about this far inside its window (measured on click tracks).
    /// </summary>
    public const int OnsetLeadSamples = 1700;

    /// <summary>Beats of a bar.</summary>
    public const int BeatsPerBar = 4;

    /// <summary>The window of the loudness measure (seconds).</summary>
    public const double EnergyWindowSeconds = 0.5;

    /// <summary>The opening and each part after it that it is compared with (seconds).</summary>
    public const double EntryPartSeconds = 5;

    /// <summary>How many parts after an opening it is compared with.</summary>
    public const int EntryParts = 3;

    /// <summary>A clean opening is at least this share of the parts after it (a quiet intro is not one).</summary>
    public const double EntryCleanShare = 0.8;

    /// <summary>A window quieter than this share of the track's median loudness is silence (the quiet end is not looped).</summary>
    public const double TailSilenceShare = 0.1;

    /// <summary>The shortest part a loop repeats (seconds).</summary>
    public const double MinLoopSeconds = 8;

    /// <summary>The shortest cross-fade between two copies (seconds).</summary>
    public const double MinCrossfadeSeconds = 0.3;

    /// <summary>The longest cross-fade between two copies (seconds).</summary>
    public const double MaxCrossfadeSeconds = 2;

    /// <summary>The most copies of a loop.</summary>
    public const int MaxCopies = 40;

    /// <summary>
    /// The share of narration silence that may be cut to bring a scene change onto a beat: some silence stays before the next scene (seconds).
    /// </summary>
    public const double KeepSilenceSeconds = 0.08;

    /// <summary>A scene change in music this much louder than its median takes a shorter transition (F1).</summary>
    public const double LoudShare = 1.25;

    /// <summary>The share of a transition's seconds kept where the music is loud.</summary>
    public const double LoudTransitionShare = 0.75;

    /// <summary>The fade of a music start or end marked "fade" (seconds).</summary>
    public const double WindowFadeSeconds = 1.5;
}

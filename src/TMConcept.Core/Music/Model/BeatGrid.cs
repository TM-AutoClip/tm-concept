namespace TMConcept.Core.Music;

/// <summary>The tempo and phase of a track (F7, <see cref="BeatTracker"/>): a beat every <see cref="BeatSeconds"/> from <see cref="FirstBeatSeconds"/>.</summary>
/// <param name="Bpm">Beats per minute (70–170).</param>
/// <param name="FirstBeatSeconds">The first beat (seconds from the track's start, below one beat).</param>
public sealed record BeatGrid(double Bpm, double FirstBeatSeconds)
{
    /// <summary>Seconds of one beat.</summary>
    public double BeatSeconds => 60 / Bpm;

    /// <summary>Seconds of one bar (four beats).</summary>
    public double BarSeconds => BeatSeconds * MusicLibraryRules.BeatsPerBar;
}

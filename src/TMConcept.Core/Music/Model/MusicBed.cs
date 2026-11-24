namespace TMConcept.Core.Music;

/// <summary>
/// How a track is laid under a video (F8, <see cref="MusicBedPlanner"/>): it starts at <see cref="EntrySeconds"/> (a bar line, the strongest
/// clean opening); when what is left is shorter than the video, the part from the entry to <see cref="LoopEndSeconds"/> (a bar line before
/// the track's quiet end) is played <see cref="Copies"/> times, each copy joined to the next with a cross-fade of
/// <see cref="CrossfadeSeconds"/>. The result lasts at least <see cref="LengthSeconds"/>.
/// </summary>
/// <param name="EntrySeconds">Where the track starts.</param>
/// <param name="LoopEndSeconds">Where each copy ends.</param>
/// <param name="Copies">How many copies (1 = no loop).</param>
/// <param name="CrossfadeSeconds">The cross-fade between two copies (0 without a loop).</param>
/// <param name="LengthSeconds">The length that is needed.</param>
public sealed record MusicBed(double EntrySeconds, double LoopEndSeconds, int Copies, double CrossfadeSeconds, double LengthSeconds)
{
    /// <summary>The length the copies give: every copy less the cross-fades between them.</summary>
    public double TotalSeconds => (Copies * (LoopEndSeconds - EntrySeconds)) - ((Copies - 1) * CrossfadeSeconds);
}

namespace TMConcept.Core.Music;

/// <summary>The loudness of a track over time (F8): the RMS of each window of <see cref="WindowSeconds"/>.</summary>
/// <param name="Rms">The RMS of every window, in order.</param>
/// <param name="WindowSeconds">The window's length.</param>
public sealed record MusicEnergy(IReadOnlyList<double> Rms, double WindowSeconds)
{
    /// <summary>The track's length the windows cover.</summary>
    public double Seconds => Rms.Count * WindowSeconds;
}

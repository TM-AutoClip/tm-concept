namespace TMConcept.Core.Videos;

/// <summary>
/// How a run's background music is laid under its video (0.1.45, <see cref="VideoMusicStep.SettleAsync"/>): the track made from its entry
/// point with its loop copies (<see cref="Music.MusicBed"/>, F8; null when ffmpeg could not make it: the track plays from its start), and
/// the scenes it plays under (the scene plan's hints).
/// </summary>
/// <param name="BedFile">The laid-out track in the work folder; null = the track itself.</param>
/// <param name="BedSeconds">The laid-out track's length (0 without one).</param>
/// <param name="StartSeconds">Where the music starts in the video.</param>
/// <param name="EndSeconds">Where it ends; null = with the video.</param>
/// <param name="FadeInSeconds">Its fade-in.</param>
/// <param name="EndFadeSeconds">Its fade-out before <paramref name="EndSeconds"/> (unused when it ends with the video: the template's fade-out).</param>
public sealed record MusicPlacement(string? BedFile, double BedSeconds, double StartSeconds, double? EndSeconds, double FadeInSeconds, double EndFadeSeconds);

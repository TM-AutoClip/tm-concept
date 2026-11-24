namespace TMConcept.Core.Media;

/// <summary>The background music the mux lays under the narration (0.1.44, C3).</summary>
/// <param name="File">The music file (a full path; it is in the run's work folder).</param>
/// <param name="Loop">The track is shorter than the video: it is played again from its start until the video ends.</param>
/// <param name="DurationSeconds">The video's length: the music is cut there.</param>
/// <param name="Volume">The level as an ffmpeg <c>volume</c> expression of <c>t</c> (<see cref="MusicEnvelope.Expression"/>).</param>
/// <param name="FadeOutSeconds">The fade-out at the end (0 = none).</param>
/// <param name="StartSeconds">Where the music starts in the video (0.1.45, the scene plan's hint; 0 = with the video).</param>
/// <param name="EndSeconds">Where the music ends in the video (0.1.45; null = with the video). The fade-out ends there.</param>
/// <param name="FadeInSeconds">The fade-in at the music's start (0.1.45; 0 = none).</param>
public sealed record MuxMusic(string File, bool Loop, double DurationSeconds, string Volume, double FadeOutSeconds, double StartSeconds = 0,
    double? EndSeconds = null, double FadeInSeconds = 0);

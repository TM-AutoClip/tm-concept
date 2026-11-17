namespace TMConcept.Core.Styles;

/// <summary>What the app draws over a video with a template (<see cref="StyleOverlay.Build"/>).</summary>
/// <param name="Ass">The ASS text burned in at the mux (subtitles, progress bar, closing card text); <c>null</c> when there is nothing to draw.</param>
/// <param name="Card">The closing card, when the template shows one; the channel photo is laid over it by ffmpeg.</param>
public sealed record StyleOverlayResult(string? Ass, EndCardLayout? Card);

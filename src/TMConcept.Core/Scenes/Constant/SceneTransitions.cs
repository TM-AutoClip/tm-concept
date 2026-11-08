namespace TMConcept.Core.Scenes;

/// <summary>
/// How a scene begins (kept in <c>input.json</c> as <c>transition</c>): <see cref="None"/> = no placeholder (a cut, the video code decides
/// nothing else), <see cref="Cut"/> = a hard cut asked for (the video code is told), <see cref="Fade"/> = a short fade through black that
/// ffmpeg adds at the scene's start after the render, whatever the code drew. The first scene has no transition.
/// </summary>
public static class SceneTransitions
{
    /// <summary>No placeholder.</summary>
    public const string None = "";

    /// <summary>A hard cut.</summary>
    public const string Cut = "cut";

    /// <summary>A fade through black.</summary>
    public const string Fade = "fade";

    /// <summary>
    /// Length of each half of a fade: the picture goes dark over this time before the scene's start and comes back over the same time after
    /// it (about half a second in all); the timing of the scenes does not change.
    /// </summary>
    public const int FadeHalfMs = 250;
}

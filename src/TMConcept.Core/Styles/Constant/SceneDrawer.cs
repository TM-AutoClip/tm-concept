namespace TMConcept.Core.Styles;

/// <summary>
/// Who draws a template's HyperFrames videos (0.1.33). <see cref="FreeCode"/>: the video code model writes the animation (the way before
/// 0.1.33, and what a template saved before reads as). <see cref="App"/>: the model writes only each scene's plan (base, points, beats) and
/// the app's own overlay library draws it (<see cref="Plans.PlanDrawer"/>); a new template starts with it.
/// </summary>
public enum SceneDrawer
{
    /// <summary>"Serbest kod": the model writes the code.</summary>
    FreeCode = 0,

    /// <summary>The app's drawer from the model's scene plan.</summary>
    App = 1,
}

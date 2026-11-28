namespace TMConcept.Core.Styles;

/// <summary>
/// A profile's (a series') look of the video (28 Nov 2026, the developer: "şablondan kaldıralım dedim … bu video üretim seçimi gibi bir şey
/// bunu video kodu ile ayarlamalı"): the drawing style, how much things move and the style's finishing layer. Chosen on the video page next to
/// the video code model, kept per profile (<see cref="IVideoLookStore"/>) and put into each starting video's template copy
/// (<see cref="VideoStyleRules.Snapshot"/>), so a change in the middle of the queue reaches only the next video. Templates no longer hold
/// them; a storytelling style adapts the scenes while the video is made (<see cref="DrawStyleDefinition.SceneGuidance"/>), and concepts are
/// written for no style.
/// </summary>
public sealed class VideoLook
{
    /// <summary>The drawing style (<see cref="DrawStyles"/>); <see cref="DrawStyle.Auto"/> = the model chooses one that fits each video.</summary>
    public DrawStyle DrawStyle { get; set; } = DrawStyle.Auto;

    /// <summary>How much things move.</summary>
    public MotionIntensity Motion { get; set; } = MotionIntensity.Normal;

    /// <summary>The style's finishing layer (paper tooth, grain, vignette, glow, shadow, boil; the app's drawer only). On by default.</summary>
    public bool Finish { get; set; } = true;
}

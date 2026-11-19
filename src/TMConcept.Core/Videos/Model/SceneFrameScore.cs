namespace TMConcept.Core.Videos;

/// <summary>
/// The frame check of one scene (<see cref="FrameCheck"/>): the brightness deviation of the frame from its middle, whether that frame is
/// near-uniform, and with a scene image the best resemblance to it (null without an image, or when the image itself is near-uniform and so
/// cannot be told apart). <see cref="Failed"/> when the frame is near-uniform although its image is not, or does not resemble its image.
/// </summary>
public sealed record SceneFrameScore(int Scene, double Deviation, double? Resemblance, bool Uniform)
{
    /// <summary>Whether the scene failed the check.</summary>
    public bool Failed => Uniform || Resemblance < FrameCheckLimits.MinResemblance;

    /// <summary>The finding in English (log and error text).</summary>
    public string Finding => Uniform
        ? FormattableString.Invariant($"scene {Scene} shows an empty frame (deviation {Deviation:0.0})")
        : FormattableString.Invariant($"scene {Scene} does not show its image (resemblance {Resemblance:0.00})");
}

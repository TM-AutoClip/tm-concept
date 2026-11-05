namespace TMConcept.Core.Images;

/// <summary>Where scene images come from.</summary>
public static class ImageSources
{
    /// <summary>No images (drawn scenes only).</summary>
    public const string None = "none";

    /// <summary>The profile's sample images.</summary>
    public const string Samples = "samples";

    /// <summary>An image model (the image step), with prompts written by the image prompt step.</summary>
    public const string Model = "model";
}

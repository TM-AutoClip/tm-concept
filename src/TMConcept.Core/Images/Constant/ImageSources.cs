namespace TMConcept.Core.Images;

/// <summary>Where scene images come from.</summary>
public static class ImageSources
{
    /// <summary>No images (drawn scenes only).</summary>
    public const string None = "none";

    /// <summary>
    /// The profile's sample images: written on videos and queue items until 0.1.28 (sample images were removed in 0.1.29). Only read, to name
    /// the image source of such a video; a queued item with it is made without images.
    /// </summary>
    public const string Samples = "samples";

    /// <summary>An image model (the image step), with prompts written by the image prompt step.</summary>
    public const string Model = "model";
}

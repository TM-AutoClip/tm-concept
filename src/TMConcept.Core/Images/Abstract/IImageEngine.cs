namespace TMConcept.Core.Images;

/// <summary>An online image model family of one provider (from DI; the provider code picks it).</summary>
public interface IImageEngine
{
    /// <summary>Provider code.</summary>
    string Code { get; }

    /// <summary>Makes one image from a prompt into <see cref="ImageRequest.Output"/>.</summary>
    Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken cancellationToken);
}

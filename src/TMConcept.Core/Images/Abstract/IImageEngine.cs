using TMConcept.Core.Models;

namespace TMConcept.Core.Images;

/// <summary>An image model family of one provider, online or local (from DI; the provider code picks it).</summary>
public interface IImageEngine
{
    /// <summary>Provider code.</summary>
    string Code { get; }

    /// <summary>Makes one image from a prompt into <see cref="ImageRequest.Output"/>.</summary>
    Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Why <paramref name="model"/> cannot make pictures on this computer now (null = nothing known against it). Cheap and free: the video
    /// asks it while it prepares, before the paid image prompt call (an online engine has nothing to check there).
    /// </summary>
    ImageEngineProblem? Problem(ResolvedModel model);
}

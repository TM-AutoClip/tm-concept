using TMConcept.Core.Models;

namespace TMConcept.Core.Images;

/// <summary>A prompt, the wanted size, the output file (png/jpg) and the resolved model with its key.</summary>
public sealed record ImageRequest(string Prompt, int Width, int Height, string Output, ResolvedModel Model)
{
    /// <summary>
    /// What the picture must not show (null = the model's own default). Only models guided with a scale above 1 read it (Stable Diffusion
    /// 3.5, Qwen-Image); the distilled ones (FLUX klein, Z-Image Turbo, SDXL Lightning) ignore it.
    /// </summary>
    public string? NegativePrompt { get; init; }
}

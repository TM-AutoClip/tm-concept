using TMConcept.Core.Tools;

namespace TMConcept.Engines.Images;

/// <summary>
/// A local image model run by stable-diffusion.cpp: its pinned weight files (diffusion model, text encoder, VAE) and the sampler steps.
/// The memory flags come from the card the image build runs on (<see cref="ImageMemoryProfile"/>), not from the model.
/// </summary>
/// <param name="Id">Model id (the catalog's <c>local/&lt;id&gt;</c>).</param>
/// <param name="Name">Display name.</param>
/// <param name="Diffusion">Diffusion model file (<c>--diffusion-model</c>).</param>
/// <param name="TextEncoder">Text encoder file (<c>--llm</c>).</param>
/// <param name="Vae">VAE file (<c>--vae</c>).</param>
/// <param name="Steps">Sampling steps.</param>
public sealed record LocalImageModel(string Id, string Name, ToolArchive Diffusion, ToolArchive TextEncoder, ToolArchive Vae, int Steps)
{
    /// <summary>Every weights file.</summary>
    public IReadOnlyList<ToolArchive> Files => [Diffusion, TextEncoder, Vae];
}

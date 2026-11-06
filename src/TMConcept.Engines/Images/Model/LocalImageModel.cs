using TMConcept.Core.Tools;

namespace TMConcept.Engines.Images;

/// <summary>
/// A local image model run by stable-diffusion.cpp: its pinned weight files (diffusion model, text encoder, VAE), the sampler settings and
/// the memory flags (the prototype's measured profile).
/// </summary>
/// <param name="Id">Model id (the catalog's <c>local/&lt;id&gt;</c>).</param>
/// <param name="Name">Display name.</param>
/// <param name="Diffusion">Diffusion model file (<c>--diffusion-model</c>).</param>
/// <param name="TextEncoder">Text encoder file (<c>--llm</c>).</param>
/// <param name="Vae">VAE file (<c>--vae</c>).</param>
/// <param name="Steps">Sampling steps.</param>
/// <param name="Flags">Memory flags.</param>
public sealed record LocalImageModel(string Id, string Name, ToolArchive Diffusion, ToolArchive TextEncoder, ToolArchive Vae, int Steps, IReadOnlyList<string> Flags)
{
    /// <summary>Every weights file.</summary>
    public IReadOnlyList<ToolArchive> Files => [Diffusion, TextEncoder, Vae];
}

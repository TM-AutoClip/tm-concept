using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Images;

/// <summary>
/// A local image model run by stable-diffusion.cpp (sd-cli): its pinned weight files, each with its role (diffusion model or whole
/// checkpoint, text encoders, VAE), how it samples (steps, guidance scale, sampler, scheduler, flow shift, extra options of its family),
/// the largest picture it is made for, its license and its graphics card memory class. The memory options come from the card's free memory
/// at run time (<see cref="ImageMemoryPlanner"/>), not from the model.
/// </summary>
/// <param name="Id">Model id (the catalog's <c>local/&lt;id&gt;</c>).</param>
/// <param name="Name">Display name (the catalog's name).</param>
/// <param name="Weights">The weights files with their roles; exactly one is the diffusion model or the whole checkpoint.</param>
public sealed record LocalImageModel(string Id, string Name, IReadOnlyList<ImageModelFile> Weights)
{
    /// <summary>The family (<c>z-image</c>, <c>flux2</c>, <c>flux3</c>, <c>sd3</c>, <c>sdxl</c>, <c>qwen-image</c>), for the log and the tests.</summary>
    public string Family { get; init; } = string.Empty;

    /// <summary>Sampling steps.</summary>
    public int Steps { get; init; } = 8;

    /// <summary>Guidance scale (<c>--cfg-scale</c>): 1 for the distilled models; above 1 the negative prompt is used.</summary>
    public double CfgScale { get; init; } = 1.0;

    /// <summary>Sampler (<c>--sampling-method</c>).</summary>
    public string Sampler { get; init; } = "euler";

    /// <summary>Scheduler (<c>--scheduler</c>); null = sd-cli's default for the family.</summary>
    public string? Scheduler { get; init; }

    /// <summary>Flow shift (<c>--flow-shift</c>); null = sd-cli's default for the family.</summary>
    public double? FlowShift { get; init; }

    /// <summary>Further options of the family (Stable Diffusion 3.5: <c>--slg-scale 2.5</c>).</summary>
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];

    /// <summary>The largest picture in pixels the model is made for (0 = no limit besides the long side); SDXL and SD 3.5: about one megapixel.</summary>
    public long MaxPixels { get; init; }

    /// <summary>What the picture must not show when the request names nothing (used only when <see cref="CfgScale"/> is above 1).</summary>
    public string NegativePrompt { get; init; } = string.Empty;

    /// <summary>License code (the catalog's <see cref="CatalogModel.License"/>).</summary>
    public string License { get; init; } = "apache-2.0";

    /// <summary>What the license allows (the catalog's <see cref="CatalogModel.LicenseUse"/>).</summary>
    public LicenseUse LicenseUse { get; init; }

    /// <summary>Where the license is (the catalog's <see cref="CatalogModel.LicenseUrl"/>).</summary>
    public string LicenseUrl { get; init; } = string.Empty;

    /// <summary>The smallest graphics card class it runs in (the catalog's <see cref="CatalogModel.MinVideoMemoryGb"/>).</summary>
    public VramTier Tier { get; init; } = VramTier.Gb6;

    /// <summary>The oldest stable-diffusion.cpp build (the number in <c>master-&lt;n&gt;-&lt;commit&gt;</c>) that knows the family (0 = any).</summary>
    public int MinSdCppBuild { get; init; }

    /// <summary>Every weights file.</summary>
    public IReadOnlyList<ToolArchive> Files => Weights.Select(w => w.File).ToList();

    /// <summary>The diffusion model (or the whole checkpoint): the largest part that must reach the graphics card.</summary>
    public ToolArchive Diffusion => Weights.First(w => w.Role is ImageFileRole.Diffusion or ImageFileRole.Full).File;

    /// <summary>The size of every weights file together.</summary>
    public long TotalBytes => Weights.Sum(w => w.File.ApproximateBytes);
}

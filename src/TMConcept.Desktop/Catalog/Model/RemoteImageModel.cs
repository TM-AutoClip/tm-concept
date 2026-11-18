namespace TMConcept.Desktop.Catalog;

/// <summary>
/// A local image model for stable-diffusion.cpp: the typed fields of a built-in one (no free command-line arguments), each weights file pinned
/// to a Hugging Face commit with its SHA-256 and size.
/// </summary>
public sealed class RemoteImageModel
{
    /// <summary>The engine that runs it; only <c>sd-cpp</c> (stable-diffusion.cpp) exists.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>Model id (lower case, digits, dots and dashes).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The family (one a built-in model has: <c>z-image</c>, <c>flux2</c> …).</summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>Sampling steps.</summary>
    public int Steps { get; set; }

    /// <summary>Guidance scale.</summary>
    public double CfgScale { get; set; } = 1.0;

    /// <summary>Sampler (one a built-in model uses).</summary>
    public string Sampler { get; set; } = string.Empty;

    /// <summary>Scheduler (one a built-in model uses), or null for sd-cli's default.</summary>
    public string? Scheduler { get; set; }

    /// <summary>Flow shift, or null for sd-cli's default.</summary>
    public double? FlowShift { get; set; }

    /// <summary>The largest picture in pixels (0 = no limit besides the long side).</summary>
    public long MaxPixels { get; set; }

    /// <summary>Negative prompt for a guidance scale above 1.</summary>
    public string NegativePrompt { get; set; } = string.Empty;

    /// <summary>License code.</summary>
    public string License { get; set; } = string.Empty;

    /// <summary>What the license allows: <c>Commercial</c>, <c>Limited</c> or <c>NonCommercial</c> (anything but free commercial use is asked before the download).</summary>
    public string LicenseUse { get; set; } = string.Empty;

    /// <summary>Where the license is (https; required unless the use is <c>Commercial</c>).</summary>
    public string LicenseUrl { get; set; } = string.Empty;

    /// <summary>The smallest graphics card class in GB (4, 6, 8, 12, 16, 24 or 32).</summary>
    public int Tier { get; set; }

    /// <summary>The oldest stable-diffusion.cpp build that knows the model (0 = any).</summary>
    public int MinSdCppBuild { get; set; }

    /// <summary>The weights files.</summary>
    public List<RemoteImageFile>? Files { get; set; }
}

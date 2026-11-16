using System.Globalization;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Images;

/// <summary>
/// The memory flags of sd-cli for the card the chosen build runs on (<c>--diffusion-fa</c> is always passed by the engine, not here).
/// Up to 8 GB or unknown: the prototype's profile measured on a 6 GB RTX 3060 Laptop (tiled VAE, a 4 GB graph budget). Above 8 and below
/// 12 GB: tiled VAE and a budget of the card's size less 2 GB (not measured; sd.cpp's <c>--max-vram</c> is the budget for weights and
/// buffers of its automatic graph cutting). From 12 GB: no limit, sd.cpp places the weights by the free memory itself (its automatic
/// placement is on by default at master-890). The processor build gets none. A card's size is read with 0.25 GB of slack: a "12 GB" card
/// reports 11.99 GiB.
/// </summary>
public static class ImageMemoryProfile
{
    /// <summary>The flags of the measured 6 GB profile.</summary>
    public static readonly IReadOnlyList<string> Small = ["--vae-tiling", "--max-vram", "4"];

    /// <summary>Up to this size (GB) the small profile is used.</summary>
    public const double SmallUpToGb = 8;

    /// <summary>From this size (GB) no memory limit is passed.</summary>
    public const double LargeFromGb = 12;

    /// <summary>The flags for <paramref name="choice"/>.</summary>
    public static IReadOnlyList<string> Flags(ImageBackendChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Backend == ImageBackend.Cpu)
        {
            return [];
        }

        if (choice.Device?.VideoMemoryBytes is not { } bytes)
        {
            return Small;
        }

        var gigabytes = bytes / (1024.0 * 1024 * 1024) + 0.25;
        if (gigabytes <= SmallUpToGb + 0.5)
        {
            return Small;
        }

        return gigabytes >= LargeFromGb ? [] : ["--vae-tiling", "--max-vram", ((int)Math.Floor(gigabytes) - 2).ToString(CultureInfo.InvariantCulture)];
    }
}

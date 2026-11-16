using System.Globalization;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Images;

/// <summary>
/// sd-cli's memory options for one picture, from the FREE graphics memory measured just before the run (<see cref="IVramProbe"/>, inside the
/// graphics card lease) and the free main memory (research 0.1.17, sd.cpp docs/guides/performance.md at master-1106):
/// <list type="bullet">
/// <item>Every card run tiles the VAE (<c>--vae-tiling</c>: the 16 GB AMD card failed its untiled VAE in 0.1.16) and keeps a reserve free
/// for the system and other programs (<c>--max-vram -R</c>: sd.cpp takes its own reading of the free memory minus R GiB). R = max(1 GiB, the
/// card's size / 16) when the free memory was measured, the card's size / 8 when it was only estimated.</item>
/// <item><see cref="ImageMemoryLevel.Card"/>: the diffusion model and <see cref="WorkingBytes"/> fit in the free memory less R; sd-cli places
/// the text encoder itself (on the card or in main memory).</item>
/// <item><see cref="ImageMemoryLevel.Offload"/>: they do not, but <see cref="WorkingBytes"/> does and main memory holds every weights file
/// with <see cref="MainMemoryHeadroomBytes"/> to spare (unknown main memory is not held against it): <c>--offload-to-cpu</c>, the
/// weights go to the card piece by piece.</item>
/// <item>Otherwise <see cref="ImageMemoryLevel.Refused"/> with the figures and what to do (close programs, a lower memory class, an online model).</item>
/// <item>A run that ends out of memory (<see cref="IsOutOfMemory"/>) steps down one level (<see cref="StepDown"/>): card → offload → offload
/// with the text encoder on the processor; it is tried once more.</item>
/// </list>
/// A card whose memory is unknown gets the offload level with a 1 GiB reserve. The processor build gets no memory options.
/// </summary>
public static class ImageMemoryPlanner
{
    /// <summary>One GiB.</summary>
    public const long Gibibyte = 1024L * 1024 * 1024;

    /// <summary>The working memory of a run besides the weights on the card: sd.cpp's 2 GiB compute reserve and about 0.5 GiB for the tiled VAE.</summary>
    public const long WorkingBytes = 2560L * 1024 * 1024;

    /// <summary>Main memory left over when the weights are kept there (sd.cpp keeps max(2 GiB, 10 %) itself).</summary>
    public const long MainMemoryHeadroomBytes = 2 * Gibibyte;

    /// <summary>Texts in sd-cli's output that mean the graphics card (or main memory) ran out.</summary>
    private static readonly string[] OutOfMemoryTexts =
    [
        "out of memory", "outofdevicememory", "cannot make enough memory available", "failed to allocate", "cudamalloc failed", "unable to allocate",
        "not enough memory",
    ];

    /// <summary>The plan for a run of <paramref name="model"/> with the <paramref name="backend"/> build.</summary>
    /// <param name="model">The model.</param>
    /// <param name="backend">The build.</param>
    /// <param name="memory">The card's memory read just before (ignored for the processor build).</param>
    /// <param name="freeMainMemory">Free main memory (null = unknown).</param>
    public static ImageMemoryPlan Plan(LocalImageModel model, ImageBackend backend, VramReading memory, long? freeMainMemory)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(memory);
        if (backend == ImageBackend.Cpu)
        {
            return new ImageMemoryPlan(ImageMemoryLevel.Processor, [], 0, string.Empty);
        }

        if (memory.TotalBytes <= 0)
        {
            return For(ImageMemoryLevel.Offload, 1);
        }

        var reserve = Reserve(memory);
        var reserveGib = (int)Math.Ceiling(reserve / (double)Gibibyte);
        var usable = memory.FreeBytes - (long)reserveGib * Gibibyte;
        if (model.Diffusion.ApproximateBytes + WorkingBytes <= usable)
        {
            return For(ImageMemoryLevel.Card, reserveGib);
        }

        // This app's own budget (DXGI) alone never refuses: Windows may lower it while the window is in the background, and sd-cli has its
        // own. When the memory free across all processes is larger, the offload level is tried; an out-of-memory end still steps down.
        var mainNeed = model.TotalBytes + MainMemoryHeadroomBytes;
        var offloadUsable = Math.Max(usable, (memory.SystemFreeBytes ?? 0) - ((long)reserveGib * Gibibyte));
        if (offloadUsable >= WorkingBytes && (freeMainMemory is null || freeMainMemory >= mainNeed))
        {
            return For(ImageMemoryLevel.Offload, reserveGib);
        }

        return new ImageMemoryPlan(ImageMemoryLevel.Refused, [], reserveGib, Refusal(model, memory, reserveGib, usable, freeMainMemory, mainNeed));
    }

    /// <summary>The next lower level after an out-of-memory end (null: there is none).</summary>
    public static ImageMemoryPlan? StepDown(ImageMemoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Level switch
        {
            ImageMemoryLevel.Card => For(ImageMemoryLevel.Offload, plan.ReserveGib),
            ImageMemoryLevel.Offload => For(ImageMemoryLevel.OffloadTextEncoder, plan.ReserveGib),
            _ => null,
        };
    }

    /// <summary>The graphics memory kept free: max(1 GiB, size / 16) when measured, size / 8 when estimated.</summary>
    public static long Reserve(VramReading memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return memory.Measured ? Math.Max(Gibibyte, memory.TotalBytes / 16) : memory.TotalBytes / 8;
    }

    /// <summary>Whether sd-cli's output says the memory ran out.</summary>
    public static bool IsOutOfMemory(string output) =>
        !string.IsNullOrEmpty(output) && OutOfMemoryTexts.Any(t => output.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static ImageMemoryPlan For(ImageMemoryLevel level, int reserveGib)
    {
        var flags = new List<string> { "--vae-tiling", "--max-vram", "-" + reserveGib.ToString(CultureInfo.InvariantCulture) };
        if (level is ImageMemoryLevel.Offload or ImageMemoryLevel.OffloadTextEncoder)
        {
            flags.Add("--offload-to-cpu");
        }

        if (level == ImageMemoryLevel.OffloadTextEncoder)
        {
            flags.AddRange(["--backend", "te=cpu"]);
        }

        return new ImageMemoryPlan(level, flags, reserveGib, string.Empty);
    }

    private static string Refusal(LocalImageModel model, VramReading memory, int reserveGib, long usable, long? freeMainMemory, long mainNeed)
    {
        static string Gib(long bytes) => (Math.Max(0, bytes) / (double)Gibibyte).ToString("0.0", CultureInfo.InvariantCulture);
        var card = $"{Gib(memory.FreeBytes)} of the card's {Gib(memory.TotalBytes)} GiB are free{(memory.Measured ? string.Empty : " (estimated)")} and "
            + $"{reserveGib.ToString(CultureInfo.InvariantCulture)} GiB stay free for the system";
        var reason = usable < WorkingBytes
            ? $"it needs at least {Gib(WorkingBytes)} GiB of graphics memory to work in, but {card}"
            : $"its {Gib(model.Diffusion.ApproximateBytes)} GiB diffusion model does not fit on the card ({card}), and keeping its weights in main memory needs "
                + $"{Gib(mainNeed)} GiB free there ({Gib(freeMainMemory ?? 0)} GiB are free)";
        return $"{model.Name} cannot run on this computer now: {reason}. Close programs that use the graphics card or memory (games, browsers, other AI "
            + "programs), or choose an image model of a lower memory class, or an online image model.";
    }
}

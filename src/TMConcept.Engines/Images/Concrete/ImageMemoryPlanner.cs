using System.Globalization;
using TMConcept.Core.Hardware;

namespace TMConcept.Engines.Images;

/// <summary>
/// sd-cli's memory options for one picture, from the FREE graphics memory measured just before the run (<see cref="IVramProbe"/>, inside the
/// graphics card lease), the free main memory and the model's own figures for this card (<see cref="ImageMemorySettings"/>, from its class:
/// <see cref="SdCppModelBase.MemorySettings"/>). sd.cpp facts: docs/guides/performance.md and <c>sd-cli --help</c> at master-1106.
/// <para>The order (0.1.18, the developer's rule; the MODEL never changes, only where its weights live):</para>
/// <list type="number">
/// <item><see cref="ImageMemoryLevel.Card"/>: the diffusion model and <see cref="ImageMemorySettings.CardWorkingBytes"/> fit in the free
/// memory less the reserve R: <c>--vae-tiling --max-vram -R</c>, sd.cpp places the rest itself (auto-fit).</item>
/// <item><see cref="ImageMemoryLevel.MainMemory"/>: it does not fit, but main memory holds what the card cannot (every weight beyond the
/// card's room, plus <see cref="ImageMemorySettings.MainMemoryHeadroomBytes"/>): sd.cpp's own placement (auto-fit: the card first, then main
/// memory) with the weights memory-mapped, <c>--mmap</c>. No <c>--offload-to-cpu</c>: it is an explicit params backend and turns auto-fit
/// off (0.1.17 did that and then demanded every weight plus 2 GiB free in main memory: the Z-Image Q8 refusal on the 6 GB laptop). A card
/// that overflows by 100 MB uses main memory, not the disk.</item>
/// <item><see cref="ImageMemoryLevel.Disk"/>: main memory is short too: the diffusion weights are read from the disk when needed, the text
/// encoder and VAE stay memory-mapped in main memory (<c>--params-backend diffusion=disk,te=cpu,vae=cpu</c>). Automatic, no question.</item>
/// <item><see cref="ImageMemoryLevel.Refused"/> only when even that cannot work: the card's free memory less R is below
/// <see cref="ImageMemorySettings.MinimumWorkingBytes"/>.</item>
/// </list>
/// R = max(1 GiB, the card's size / 16) when the free memory was measured, the card's size / 8 when it was only estimated. A card whose memory
/// is unknown gets the main memory level (sd.cpp's own placement) with a 1 GiB reserve; the processor build gets no memory options. A run
/// that ends out of memory (<see cref="IsOutOfMemory"/>) steps down one level at a time (<see cref="StepDown"/>): card → main memory → disk →
/// disk with the text encoder on the processor. Every level but the card is slower and the engine says so on screen.
/// </summary>
public static class ImageMemoryPlanner
{
    /// <summary>One GiB.</summary>
    public const long Gibibyte = 1024L * 1024 * 1024;

    /// <summary>The working memory of a run on the card besides the weights: sd.cpp's 2 GiB compute reserve and about 0.5 GiB for the tiled VAE.</summary>
    public const long WorkingBytes = 2560L * 1024 * 1024;

    /// <summary>The figures when a model names none: <see cref="WorkingBytes"/> on the card, 1.5 GiB off it, 1 GiB of main memory kept free.</summary>
    public static readonly ImageMemorySettings DefaultSettings = new(WorkingBytes, 1536L * 1024 * 1024, Gibibyte);

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
    /// <param name="freeMainMemory">Free main memory (null = unknown; never held against the model).</param>
    /// <param name="settings">The model's figures for this card (null = <see cref="DefaultSettings"/>).</param>
    public static ImageMemoryPlan Plan(LocalImageModel model, ImageBackend backend, VramReading memory, long? freeMainMemory, ImageMemorySettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(memory);
        var figures = settings ?? DefaultSettings;
        if (backend == ImageBackend.Cpu)
        {
            return new ImageMemoryPlan(ImageMemoryLevel.Processor, [], 0, string.Empty) { NeededMainBytes = model.TotalBytes };
        }

        if (memory.TotalBytes <= 0)
        {
            return For(ImageMemoryLevel.MainMemory, 1);
        }

        var reserveGib = (int)Math.Ceiling(Reserve(memory) / (double)Gibibyte);
        var reserve = (long)reserveGib * Gibibyte;
        var usable = memory.FreeBytes - reserve;
        var diffusion = model.Diffusion.ApproximateBytes;
        if (diffusion + figures.CardWorkingBytes <= usable)
        {
            return For(ImageMemoryLevel.Card, reserveGib) with { NeededVideoBytes = diffusion + figures.CardWorkingBytes + reserve };
        }

        // This app's own budget (DXGI) alone never refuses: Windows may lower it while the window is in the background, and sd-cli takes its
        // own reading. When the memory free across all processes is larger, that counts off the card.
        var offUsable = Math.Max(usable, (memory.SystemFreeBytes ?? 0) - reserve);
        var cardNeed = figures.MinimumWorkingBytes + reserve;
        var diskMain = model.TotalBytes - diffusion + figures.MainMemoryHeadroomBytes;
        if (offUsable < figures.MinimumWorkingBytes)
        {
            return new ImageMemoryPlan(ImageMemoryLevel.Refused, [], reserveGib, Refusal(model, memory, reserveGib, offUsable, figures))
            {
                NeededVideoBytes = cardNeed,
                NeededMainBytes = diskMain,
            };
        }

        // What the card cannot hold waits in main memory: every weight beyond the card's room after the working memory.
        var room = Math.Max(0, offUsable - figures.MinimumWorkingBytes);
        var mainNeed = Math.Max(0, model.TotalBytes - room) + figures.MainMemoryHeadroomBytes;
        return freeMainMemory is null || freeMainMemory >= mainNeed
            ? For(ImageMemoryLevel.MainMemory, reserveGib) with { NeededVideoBytes = cardNeed, NeededMainBytes = mainNeed }
            : For(ImageMemoryLevel.Disk, reserveGib) with { NeededVideoBytes = cardNeed, NeededMainBytes = diskMain };
    }

    /// <summary>The next lower level after an out-of-memory end (null: there is none). Only the options change, never the model.</summary>
    public static ImageMemoryPlan? StepDown(ImageMemoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Level switch
        {
            ImageMemoryLevel.Card => For(ImageMemoryLevel.MainMemory, plan.ReserveGib) with { NeededVideoBytes = plan.NeededVideoBytes, NeededMainBytes = plan.NeededMainBytes },
            ImageMemoryLevel.MainMemory => For(ImageMemoryLevel.Disk, plan.ReserveGib) with { NeededVideoBytes = plan.NeededVideoBytes, NeededMainBytes = plan.NeededMainBytes },
            ImageMemoryLevel.Disk => For(ImageMemoryLevel.DiskTextEncoder, plan.ReserveGib) with { NeededVideoBytes = plan.NeededVideoBytes, NeededMainBytes = plan.NeededMainBytes },
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
        if (level is ImageMemoryLevel.MainMemory or ImageMemoryLevel.Disk or ImageMemoryLevel.DiskTextEncoder)
        {
            flags.Add("--mmap");
        }

        if (level is ImageMemoryLevel.Disk or ImageMemoryLevel.DiskTextEncoder)
        {
            flags.AddRange(["--params-backend", "diffusion=disk,te=cpu,vae=cpu"]);
        }

        if (level == ImageMemoryLevel.DiskTextEncoder)
        {
            flags.AddRange(["--backend", "te=cpu"]);
        }

        return new ImageMemoryPlan(level, flags, reserveGib, string.Empty);
    }

    private static string Refusal(LocalImageModel model, VramReading memory, int reserveGib, long usable, ImageMemorySettings settings)
    {
        static string Gib(long bytes) => (Math.Max(0, bytes) / (double)Gibibyte).ToString("0.0", CultureInfo.InvariantCulture);
        return $"{model.Name} cannot run on this computer now: it needs at least {Gib(settings.MinimumWorkingBytes)} GiB of graphics memory to work in "
            + $"(even with its weights in main memory or on the disk), but {Gib(memory.FreeBytes)} of the card's {Gib(memory.TotalBytes)} GiB are free"
            + $"{(memory.Measured ? string.Empty : " (estimated)")} and {reserveGib.ToString(CultureInfo.InvariantCulture)} GiB stay free for the system "
            + $"({Gib(usable)} GiB left). Close programs that use the graphics card (games, browsers, other AI programs), or choose an image model of a "
            + "lower memory class, or an online image model.";
    }
}

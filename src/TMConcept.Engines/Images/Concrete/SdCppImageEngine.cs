using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Hardware;
using TMConcept.Core.Images;
using TMConcept.Core.Models;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Hardware;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Images;

/// <summary>
/// The local image models (<c>local/…</c>): one stable-diffusion.cpp (<c>sd-cli</c>) run per image with the build the
/// <see cref="ImageBackendSelector"/> chose (CUDA, Vulkan or processor) and a random seed. The arguments come from the model
/// (<see cref="LocalImageModel"/>: its files by role, steps, guidance scale, sampler, scheduler, flow shift, its family's extra options, the
/// negative prompt when it is guided), always flash attention in the diffusion model (<c>--flash-attn auto,diffusion=on</c>: without it
/// Vulkan on Intel gives noise, sd.cpp issue #1449), and the memory options of <see cref="ImageMemoryPlanner"/> from the card's FREE memory
/// measured just before the run (the model's own figures: <see cref="SdCppModelBase.PlanImage"/>; card → main memory → disk, never another
/// model). Every run happens inside the graphics card lease of the model (<see cref="IGpuCoordinator"/>: the previous local model, an LLM that
/// wrote the prompts, is unloaded first and the memory is checked); before every picture the memory is read again, and when even the disk
/// level cannot run, <see cref="IMemoryWaiter"/> reads it once more and then asks the user (Retry / Cancel; night mode: the video fails). A
/// level off the card is shown as a yellow line (<see cref="IMemoryNotices"/>) and logged as a warning; a run that ends out of memory is tried
/// again one memory level lower, level by level (only the options change). The picture is at most 1024 pixels on its long side (a multiple of
/// 64; smaller for a model made for about one megapixel) and scaled by the engine. Free: the cost is 0. A picture of a single colour is a
/// failure (blank pictures from some quantized weights on Vulkan, sd.cpp issue #1031). Checked before anything runs
/// (<see cref="Problem"/>, asked while a video prepares, before its paid image prompt call): on Windows the Visual C++ runtime every build
/// imports, the driver library of the chosen GPU build, a card of a lower memory class than the model's, and a build older than the
/// model's family needs. With the Automatic setting a GPU build that fails (an exit code or a blank picture) is followed by one run of the
/// processor build when that build is installed, and the session's next images go to the processor build directly: the same model, much
/// slower, so it is shown as a yellow line and logged as a warning (0.1.17 did it silently).
/// </summary>
public sealed class SdCppImageEngine(ToolInstaller tools, LocalImageStore store, ImageBackendSelector backends, IComputeRuntimeProbe probe,
    IProcessRunner runner, IGpuCoordinator gpu, IVramProbe vram, ISystemMemory memory, IMemoryWaiter waiter, ILogger<SdCppImageEngine> logger,
    IMemoryNotices? notices = null) : IImageEngine
{
    /// <summary>The long side of a generated picture.</summary>
    public const int LongSide = 1024;

    /// <summary>Windows' STATUS_DLL_NOT_FOUND (0xC0000135) as a process exit code: a library the program needs could not be loaded.</summary>
    public const int DllNotFound = -1073741515;

    /// <summary>Microsoft's permanent download address of the Visual C++ 2015–2022 redistributable for x64.</summary>
    public const string VisualCppRedistributable = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    /// <summary>Flash attention in the diffusion model, automatic elsewhere (the option of master-1043 and later; <c>--diffusion-fa</c> is deprecated there).</summary>
    public static readonly IReadOnlyList<string> FlashAttention = ["--flash-attn", "auto,diffusion=on"];

    // The GPU build that failed in this session under the Automatic setting (its images go to the processor build since).
    private ImageBackend? _failedBackend;

    /// <inheritdoc />
    public string Code => ModelProviders.Local;

    /// <inheritdoc />
    public ImageEngineProblem? Problem(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var missing = probe.MissingVisualCppRuntime();
        if (missing.Count > 0)
        {
            return new ImageEngineProblem(ImageEngineProblems.VisualCppRuntime, string.Join(", ", missing));
        }

        var choice = backends.Choose();
        var driver = choice.Backend switch
        {
            ImageBackend.Cuda => probe.CudaDriver(),
            ImageBackend.Vulkan => probe.VulkanLoader(),
            _ => true,
        };
        if (!driver)
        {
            return new ImageEngineProblem(ImageEngineProblems.Images, DriverMissing(choice.Backend));
        }

        if (LocalImageModels.All.FirstOrDefault(m => m.Id == model.Model.Id) is not { } local)
        {
            return null;
        }

        var reason = TooSmall(local, choice) ?? TooOld(local, tools.Archive(ImageBackendSelector.Tools(choice.Backend)[0])?.Version);
        return reason is null ? null : new ImageEngineProblem(ImageEngineProblems.Images, reason);
    }

    /// <inheritdoc />
    public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var model = LocalImageModels.All.FirstOrDefault(m => m.Id == request.Model.Model.Id)
            ?? throw new InvalidOperationException($"There is no local image model '{request.Model.Model.Id}'.");
        if (store.Missing(model).Count > 0)
        {
            throw new InvalidOperationException($"{model.Name} is not installed. It is installed when a video that uses it is produced (after a confirmation).");
        }

        var missing = probe.MissingVisualCppRuntime();
        if (missing.Count > 0)
        {
            logger.LogError("{Model}: the Visual C++ runtime is missing ({Files}); sd-cli is not started", model.Id, string.Join(", ", missing));
            throw new InvalidOperationException(VisualCppMissing(model.Name, missing));
        }

        var choice = backends.Choose();
        var fallback = choice.Setting == ImageAcceleration.Automatic && choice.Backend != ImageBackend.Cpu && tools.Status(ToolKind.ImageRuntimeCpu).Ready;
        if (fallback && _failedBackend == choice.Backend)
        {
            choice = choice with { Backend = ImageBackend.Cpu, Device = null };
            fallback = false;
        }

        if (TooOld(model, tools.Archive(ImageBackendSelector.Tools(choice.Backend)[0])?.Version) is { } old)
        {
            throw new InvalidOperationException(old);
        }

        // Every run inside the model's graphics card lease (a lease of the same model nests in the video's own one): the previous local
        // model (an LLM that wrote the prompts) is off the card before sd-cli starts, and nothing else comes onto it meanwhile.
        var lease = await gpu.AcquireAsync(request.Model.Model, cancellationToken).ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            var failure = await RunPlannedAsync(model, request, choice, cancellationToken).ConfigureAwait(false);
            if (failure is not null && fallback)
            {
                logger.LogWarning(LogEvents.OnScreen, "{Model}: the {Backend} build failed; the processor build makes this picture and the next ones of this "
                    + "session (the same model, much slower)", model.Id, choice.Backend);
                notices?.Report(new MemoryNotice(MemoryNoticeKind.ProcessorFallback, model.Name, request.Model.Model.Provider + "/" + model.Id));
                _failedBackend = choice.Backend;
                failure = await RunPlannedAsync(model, request, choice with { Backend = ImageBackend.Cpu, Device = null }, cancellationToken).ConfigureAwait(false);
            }

            return failure is null ? new ImageResult(request.Output, 0) : throw new InvalidOperationException(failure);
        }
    }

    /// <summary>
    /// The picture size: the wanted shape, at most <see cref="LongSide"/> on the long side and at most <paramref name="maxPixels"/> pixels
    /// (0 = no such limit), each side a multiple of 64.
    /// </summary>
    public static (int Width, int Height) Size(int width, int height, long maxPixels = 0)
    {
        var scale = Math.Min(1.0, (double)LongSide / Math.Max(1, Math.Max(width, height)));
        if (maxPixels > 0)
        {
            scale = Math.Min(scale, Math.Sqrt(maxPixels / Math.Max(1.0, (double)width * height)));
        }

        static int Nearest(double value) => Math.Max(64, (int)Math.Round(value / 64) * 64);
        static int Down(double value) => Math.Max(64, (int)Math.Floor(value / 64) * 64);
        var (w, h) = (Nearest(width * scale), Nearest(height * scale));
        return maxPixels > 0 && (long)w * h > maxPixels ? (Down(width * scale), Down(height * scale)) : (w, h);
    }

    /// <summary>
    /// The sd-cli arguments: every weights file by its role (<see cref="RoleFlag"/>), the prompt (and the negative prompt when the model is
    /// guided above 1: <paramref name="negativePrompt"/>, or the model's default), size, steps, guidance scale, sampler, scheduler and flow
    /// shift, the family's extra options, seed, output, threads, flash attention and <paramref name="memoryFlags"/>
    /// (<see cref="ImageMemoryPlanner"/>); <paramref name="file"/> gives the path of a weights file.
    /// </summary>
    public static IReadOnlyList<string> Arguments(LocalImageModel model, string prompt, string? negativePrompt, int width, int height, long seed, string output,
        Func<ToolArchive, string> file, IReadOnlyList<string> memoryFlags)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(memoryFlags);
        var arguments = new List<string>();
        foreach (var weights in model.Weights)
        {
            arguments.Add(RoleFlag(weights.Role));
            arguments.Add(file(weights.File));
        }

        arguments.AddRange(["-p", prompt]);
        var negative = string.IsNullOrWhiteSpace(negativePrompt) ? model.NegativePrompt : negativePrompt;
        if (model.CfgScale > 1 && negative.Length > 0)
        {
            arguments.AddRange(["-n", negative]);
        }

        arguments.AddRange(
        [
            "-W", width.ToString(CultureInfo.InvariantCulture),
            "-H", height.ToString(CultureInfo.InvariantCulture),
            "--steps", model.Steps.ToString(CultureInfo.InvariantCulture),
            "--cfg-scale", model.CfgScale.ToString("0.0##", CultureInfo.InvariantCulture),
            "--sampling-method", model.Sampler,
        ]);
        if (model.Scheduler is { Length: > 0 } scheduler)
        {
            arguments.AddRange(["--scheduler", scheduler]);
        }

        if (model.FlowShift is { } shift)
        {
            arguments.AddRange(["--flow-shift", shift.ToString("0.0##", CultureInfo.InvariantCulture)]);
        }

        arguments.AddRange(model.ExtraArguments);
        arguments.AddRange(
        [
            "-s", seed.ToString(CultureInfo.InvariantCulture),
            "-o", output,
            "-t", Math.Clamp(System.Environment.ProcessorCount - 2, 1, 16).ToString(CultureInfo.InvariantCulture),
        ]);
        arguments.AddRange(FlashAttention);
        arguments.AddRange(memoryFlags);
        return arguments;
    }

    /// <summary>The sd-cli option of a weights file's role.</summary>
    public static string RoleFlag(ImageFileRole role) => role switch
    {
        ImageFileRole.Diffusion => "--diffusion-model",
        ImageFileRole.Full => "-m",
        ImageFileRole.Llm => "--llm",
        ImageFileRole.ClipL => "--clip_l",
        ImageFileRole.ClipG => "--clip_g",
        ImageFileRole.T5xxl => "--t5xxl",
        ImageFileRole.Vae => "--vae",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>The build number of a stable-diffusion.cpp release tag (<c>master-1106-532d2c1</c> → 1106; 0 when it has none).</summary>
    public static int BuildNumber(string? tag)
    {
        var parts = (tag ?? string.Empty).Split('-');
        return parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    /// <summary>Why <paramref name="model"/> cannot run with the build of release <paramref name="tag"/> (null: it can, or the build is unknown).</summary>
    public static string? TooOld(LocalImageModel model, string? tag)
    {
        ArgumentNullException.ThrowIfNull(model);
        var build = BuildNumber(tag);
        return model.MinSdCppBuild > 0 && build > 0 && build < model.MinSdCppBuild
            ? $"{model.Name} needs stable-diffusion.cpp build {model.MinSdCppBuild.ToString(CultureInfo.InvariantCulture)} or newer; the installed one is {tag}. "
                + "Update TM Concept."
            : null;
    }

    /// <summary>
    /// Why the card of <paramref name="choice"/> cannot run <paramref name="model"/> at all: its memory class (<see cref="VramTiers.ForCard"/>)
    /// is below the model's (null: it can, the memory is unknown, or the processor build runs it).
    /// </summary>
    public static string? TooSmall(LocalImageModel model, ImageBackendChoice choice)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Backend == ImageBackend.Cpu || choice.Device?.VideoMemoryBytes is not { } bytes || bytes <= 0)
        {
            return null;
        }

        return VramTiers.Sees(VramTiers.ForCard(bytes), model.Tier)
            ? null
            : $"{model.Name} needs a graphics card of the {VramTiers.Gigabytes(model.Tier).ToString(CultureInfo.InvariantCulture)} GB class; this card "
                + $"({choice.Device.Name}, {(bytes / (double)ImageMemoryPlanner.Gibibyte).ToString("0.#", CultureInfo.InvariantCulture)} GB) cannot run it. "
                + "Choose an image model of a lower memory class, or an online image model.";
    }

    /// <summary>
    /// One picture with <paramref name="choice"/>'s build: the memory plan from the card's free memory now (with the wait and the user's
    /// question of <see cref="IMemoryWaiter"/> when even the disk level cannot run), one run, and on an out-of-memory end one more run per
    /// lower level. Null when it made a picture, otherwise the error text; a shortfall throws <see cref="MemoryShortfallException"/> (night
    /// mode) or <see cref="MemoryShortfallCanceledException"/> (the user's Cancel).
    /// </summary>
    private async Task<string?> RunPlannedAsync(LocalImageModel model, ImageRequest request, ImageBackendChoice choice, CancellationToken cancellationToken)
    {
        var local = SdCppModelSource.For(request.Model.Model, model, backends);
        var plan = local.PlanImage(ImageBackend.Cpu, new MemorySnapshot(VramReading.None, null));
        if (choice.Backend != ImageBackend.Cpu)
        {
            // Read before every picture: another program may have taken memory since the lease. Too little even for the disk level: read once
            // more, then the user decides (the model never changes).
            var readings = 0;
            await waiter.WaitAsync(local, null, async token =>
            {
                // A reading after the first (the second one, each Retry) asks TM Concept's own stuck local models again first: a prompt
                // model whose unload failed may be what holds the card, and nothing else asks it while this lease is held.
                IReadOnlyList<string> stuck = readings++ == 0 ? [] : await gpu.RetryStuckAsync(local, token).ConfigureAwait(false);
                var reading = await vram.MeasureAsync(choice.Device, token).ConfigureAwait(false);
                var free = memory.AvailableBytes();
                plan = local.PlanImage(choice.Backend, new MemorySnapshot(reading, free));
                logger.LogInformation("{Model}: memory plan {Level} ({Free:0.0} of {Total:0.0} GiB graphics memory free, {Source}; {Ram} GiB main memory free)",
                    model.Id, plan.Level, reading.FreeBytes / (double)ImageMemoryPlanner.Gibibyte, reading.TotalBytes / (double)ImageMemoryPlanner.Gibibyte,
                    reading.Source, free is { } bytes ? (bytes / (double)ImageMemoryPlanner.Gibibyte).ToString("0.0", CultureInfo.InvariantCulture) : "?");
                return plan.Level != ImageMemoryLevel.Refused
                    ? null
                    : new MemoryShortfall(model.Name, reading.TotalBytes, Math.Max(reading.FreeBytes, reading.SystemFreeBytes ?? 0), reading.Source,
                        plan.NeededVideoBytes, free, plan.NeededMainBytes) { HeldBy = stuck };
            }, cancellationToken).ConfigureAwait(false);
        }

        Placed(local, plan);
        var (failure, outOfMemory) = await RunAsync(model, request, choice, plan, cancellationToken).ConfigureAwait(false);
        while (failure is not null && outOfMemory && ImageMemoryPlanner.StepDown(plan) is { } lower)
        {
            // Only the options change, never the model.
            logger.LogWarning(LogEvents.OnScreen, "{Model}: sd-cli ran out of memory at {Level}; one more run at {Lower}", model.Id, plan.Level, lower.Level);
            plan = lower;
            Placed(local, plan);
            (failure, outOfMemory) = await RunAsync(model, request, choice, plan, cancellationToken).ConfigureAwait(false);
        }

        return failure;
    }

    /// <summary>A level off the card (main memory or disk): a warning in the log and a yellow line on screen (the panel shows it once per model and level).</summary>
    private void Placed(SdCppModelBase local, ImageMemoryPlan plan)
    {
        if (plan.Placement is not (MemoryPlacement.MainMemory or MemoryPlacement.Disk))
        {
            return;
        }

        logger.LogWarning(LogEvents.OnScreen, "{Model} does not fit on the graphics card; it uses {Level} ({Flags}) and makes pictures slower", local.Id, plan.Level,
            string.Join(' ', plan.Flags));
        notices?.Report(new MemoryNotice(MemoryNoticeKind.Placed, local.Name, local.Key) { Placement = plan.Placement });
    }

    /// <summary>One sd-cli run; the error text (null when it made a picture) and whether its output says the memory ran out.</summary>
    private async Task<(string? Failure, bool OutOfMemory)> RunAsync(LocalImageModel model, ImageRequest request, ImageBackendChoice choice, ImageMemoryPlan plan,
        CancellationToken cancellationToken)
    {
        var kinds = ImageBackendSelector.Tools(choice.Backend);
        var cli = tools.Require(kinds[0]);
        // The CUDA runtime libraries live in their own folder; the loader must find them next to sd-cli's own.
        var libraries = kinds.Skip(1).Select(k => Path.GetDirectoryName(tools.Require(k))!).ToList();
        var (width, height) = Size(request.Width, request.Height, model.MaxPixels);
        Directory.CreateDirectory(Path.GetDirectoryName(request.Output)!);
        var arguments = Arguments(model, request.Prompt, request.NegativePrompt, width, height, Random.Shared.NextInt64(1, int.MaxValue), request.Output,
            f => store.File(model, f), plan.Flags);
        var environment = LibraryEnvironment(OperatingSystem.IsWindows(), Path.GetDirectoryName(cli)!, libraries,
            System.Environment.GetEnvironmentVariable("PATH"), System.Environment.GetEnvironmentVariable("LD_LIBRARY_PATH"));

        logger.LogInformation("{Model}: sd-cli {Backend} build on {Device} ({Level}: {Flags})", model.Id, choice.Backend, choice.Device?.Name ?? "the processor",
            plan.Level, string.Join(' ', plan.Flags));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunAsync(new ProcessStart(cli, arguments, Path.GetDirectoryName(request.Output)!) { Environment = environment }, null,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(request.Output))
        {
            logger.LogError("{Model}: sd-cli ({Backend}) ended with code {Code} (0x{Hex:X8}) after {Seconds:0.0} s", model.Id, choice.Backend, result.ExitCode,
                result.ExitCode, watch.Elapsed.TotalSeconds);
            return (Failure(model.Name, choice.Backend, result.ExitCode, ChildOutput.Summary(result.LastLines)), ImageMemoryPlanner.IsOutOfMemory(result.LastLines));
        }

        if (PngImage.IsSingleColour(request.Output))
        {
            logger.LogError("{Model}: sd-cli ({Backend}) made a picture of a single colour after {Seconds:0.0} s", model.Id, choice.Backend,
                watch.Elapsed.TotalSeconds);
            File.Delete(request.Output);
            return (Blank(model.Name, choice.Backend), false);
        }

        logger.LogInformation("{Model}: {Width}×{Height} image in {Seconds:0.0} s", model.Id, width, height, watch.Elapsed.TotalSeconds);
        return (null, false);
    }

    /// <summary>
    /// The environment sd-cli needs to find its libraries: on Windows the PATH (the extra library folders, then sd-cli's folder, then the
    /// current PATH; Windows has no LD_LIBRARY_PATH), on Linux LD_LIBRARY_PATH (sd-cli's folder first, then the current one).
    /// </summary>
    public static IReadOnlyDictionary<string, string> LibraryEnvironment(bool windows, string cliFolder, IReadOnlyList<string> libraryFolders, string? path,
        string? libraryPath)
    {
        ArgumentNullException.ThrowIfNull(libraryFolders);
        var folders = libraryFolders.Append(cliFolder);
        return windows
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = string.Join(';', folders.Append(path ?? string.Empty)).TrimEnd(';') }
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["LD_LIBRARY_PATH"] = string.Join(':', folders.Append(libraryPath ?? string.Empty)).TrimEnd(':') };
    }

    /// <summary>
    /// The error of a failed run. With no output the exit code is all there is: 0xC0000135 means a library (DLL) could not be loaded. Every
    /// Windows build imports the Visual C++ runtime (checked before the run, but a damaged install still fails here), and a GPU build its
    /// driver's library (the CUDA build on a computer without NVIDIA's driver, the Vulkan build without a Vulkan driver), so the text names
    /// both fixes; any other code is shown in hexadecimal too (Windows status codes are read that way).
    /// </summary>
    public static string Failure(string modelName, ImageBackend backend, int exitCode, string output)
    {
        var build = BuildName(backend);
        var reason = exitCode == DllNotFound
            ? $"a required library (DLL) of the {build} build could not be loaded (exit code 0xC0000135). Install or repair the Microsoft Visual C++ "
                + $"2015–2022 runtime (x64) from {VisualCppRedistributable}; every build needs it."
                + (backend == ImageBackend.Cpu
                    ? string.Empty
                    : " If it is installed, the graphics driver this build needs may be missing: choose another Image acceleration under Settings → General (Vulkan for AMD and Intel cards, Processor without a graphics card).")
            : $"sd-cli {build} build ended with exit code {exitCode.ToString(CultureInfo.InvariantCulture)} (0x{exitCode.ToString("X8", CultureInfo.InvariantCulture)})"
                + (output.Length > 0 ? ": " + output : ", without any output. If it keeps failing, choose another Image acceleration under Settings → General.");
        return $"{modelName} could not make the image: {reason}";
    }

    /// <summary>The error when the Visual C++ runtime is missing (found before sd-cli runs).</summary>
    public static string VisualCppMissing(string modelName, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return $"{modelName} needs the Microsoft Visual C++ 2015–2022 runtime (x64), which is not installed (missing: {string.Join(", ", files)}). "
            + $"Install it from {VisualCppRedistributable} and start the video again.";
    }

    /// <summary>The error when the chosen GPU build's driver library does not load (found before anything is paid for).</summary>
    public static string DriverMissing(ImageBackend backend) => backend == ImageBackend.Cuda
        ? "The NVIDIA CUDA build is chosen, but NVIDIA's driver (CUDA) is not installed on this computer. Choose Automatic or another Image acceleration under Settings → General."
        : $"The {BuildName(backend)} build is chosen, but no Vulkan driver is installed on this computer. Update the graphics driver, or choose Processor under Settings → General → Image acceleration.";

    /// <summary>The error of a picture of a single colour.</summary>
    public static string Blank(string modelName, ImageBackend backend) =>
        $"{modelName} made a blank picture (a single colour) with the {BuildName(backend)} build. Some graphics drivers do this with these weights; "
        + "try another local image model or another Image acceleration under Settings → General.";

    private static string BuildName(ImageBackend backend) => backend switch
    {
        ImageBackend.Cuda => "NVIDIA CUDA",
        ImageBackend.Vulkan => "Vulkan",
        _ => "processor",
    };
}

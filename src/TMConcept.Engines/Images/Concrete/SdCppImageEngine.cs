using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;
using TMConcept.Core.Images;
using TMConcept.Core.Models;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Images;

/// <summary>
/// The local image models (<c>local/…</c>): one stable-diffusion.cpp (<c>sd-cli</c>) run per image with the build the
/// <see cref="ImageBackendSelector"/> chose (CUDA, Vulkan or processor), the prototype's arguments (<c>--diffusion-model</c>, <c>--llm</c>,
/// <c>--vae</c>, euler, cfg 1, its steps), always flash attention (<c>--diffusion-fa</c>: without it Vulkan on Intel gives noise, sd.cpp
/// issue #1449) and the memory flags of the card (<see cref="ImageMemoryProfile"/>), and a random seed. The picture is made at most 1024
/// pixels on its long side (a multiple of 64) and scaled by the engine. Free: the cost is 0. The pipeline runs one video at a time, so the
/// graphics card does one job at a time. A picture of a single colour is a failure (blank pictures from some quantized weights on Vulkan,
/// sd.cpp issue #1031), not a success.
/// </summary>
public sealed class SdCppImageEngine(ToolInstaller tools, LocalImageStore store, ImageBackendSelector backends, IProcessRunner runner,
    ILogger<SdCppImageEngine> logger) : IImageEngine
{
    /// <summary>The long side of a generated picture.</summary>
    public const int LongSide = 1024;

    /// <summary>Windows' STATUS_DLL_NOT_FOUND (0xC0000135) as a process exit code: a library the program needs could not be loaded.</summary>
    public const int DllNotFound = -1073741515;

    /// <inheritdoc />
    public string Code => ModelProviders.Local;

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

        var choice = backends.Choose();
        var kinds = ImageBackendSelector.Tools(choice.Backend);
        var cli = tools.Require(kinds[0]);
        // The CUDA runtime libraries live in their own folder; the loader must find them next to sd-cli's own.
        var libraries = kinds.Skip(1).Select(k => Path.GetDirectoryName(tools.Require(k))!).ToList();
        var (width, height) = Size(request.Width, request.Height);
        Directory.CreateDirectory(Path.GetDirectoryName(request.Output)!);
        var arguments = Arguments(model, request.Prompt, width, height, Random.Shared.NextInt64(1, int.MaxValue), request.Output, f => store.File(model, f),
            ImageMemoryProfile.Flags(choice));
        var environment = LibraryEnvironment(OperatingSystem.IsWindows(), Path.GetDirectoryName(cli)!, libraries,
            System.Environment.GetEnvironmentVariable("PATH"), System.Environment.GetEnvironmentVariable("LD_LIBRARY_PATH"));

        logger.LogInformation("{Model}: sd-cli {Backend} build on {Device}", model.Id, choice.Backend, choice.Device?.Name ?? "the processor");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunAsync(new ProcessStart(cli, arguments, Path.GetDirectoryName(request.Output)!) { Environment = environment }, null,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(request.Output))
        {
            logger.LogError("{Model}: sd-cli ({Backend}) ended with code {Code} (0x{Hex:X8}) after {Seconds:0.0} s", model.Id, choice.Backend, result.ExitCode,
                result.ExitCode, watch.Elapsed.TotalSeconds);
            throw new InvalidOperationException(Failure(model.Name, choice.Backend, result.ExitCode, ChildOutput.Summary(result.LastLines)));
        }

        if (PngImage.IsSingleColour(request.Output))
        {
            logger.LogError("{Model}: sd-cli ({Backend}) made a picture of a single colour after {Seconds:0.0} s", model.Id, choice.Backend,
                watch.Elapsed.TotalSeconds);
            File.Delete(request.Output);
            throw new InvalidOperationException(Blank(model.Name, choice.Backend));
        }

        logger.LogInformation("{Model}: {Width}×{Height} image in {Seconds:0.0} s", model.Id, width, height, watch.Elapsed.TotalSeconds);
        return new ImageResult(request.Output, 0);
    }

    /// <summary>The picture size: the wanted shape, at most <see cref="LongSide"/> on the long side, each side a multiple of 64.</summary>
    public static (int Width, int Height) Size(int width, int height)
    {
        var scale = Math.Min(1.0, (double)LongSide / Math.Max(1, Math.Max(width, height)));
        static int Round(double value) => Math.Max(64, (int)Math.Round(value / 64) * 64);
        return (Round(width * scale), Round(height * scale));
    }

    /// <summary>
    /// The sd-cli arguments (the prototype's order); <paramref name="file"/> gives the path of a weights file, <paramref name="memoryFlags"/>
    /// come from the card (<see cref="ImageMemoryProfile"/>). <c>--diffusion-fa</c> is always there.
    /// </summary>
    public static IReadOnlyList<string> Arguments(LocalImageModel model, string prompt, int width, int height, long seed, string output,
        Func<ToolArchive, string> file, IReadOnlyList<string> memoryFlags)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(memoryFlags);
        var arguments = new List<string>
        {
            "--diffusion-model", file(model.Diffusion),
            "-p", prompt,
            "-W", width.ToString(CultureInfo.InvariantCulture),
            "-H", height.ToString(CultureInfo.InvariantCulture),
            "--steps", model.Steps.ToString(CultureInfo.InvariantCulture),
            "--cfg-scale", "1.0",
            "--sampling-method", "euler",
            "-s", seed.ToString(CultureInfo.InvariantCulture),
            "-o", output,
            "--llm", file(model.TextEncoder),
            "--vae", file(model.Vae),
            "-t", Math.Clamp(System.Environment.ProcessorCount - 2, 1, 16).ToString(CultureInfo.InvariantCulture),
            "--diffusion-fa",
        };
        arguments.AddRange(memoryFlags);
        return arguments;
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
    /// The error of a failed run. With no output the exit code is all there is: 0xC0000135 means a library (DLL) of the build is missing (the
    /// CUDA build on a computer without NVIDIA's driver, the Vulkan build without a Vulkan driver), so the text says to choose another Image
    /// acceleration; any other code is shown in hexadecimal too (Windows status codes are read that way).
    /// </summary>
    public static string Failure(string modelName, ImageBackend backend, int exitCode, string output)
    {
        var build = BuildName(backend);
        var reason = exitCode == DllNotFound
            ? $"a required library (DLL) is missing for the {build} build (exit code 0xC0000135). Choose another Image acceleration under Settings → General (Vulkan for AMD and Intel cards, Processor without a graphics card)."
            : $"sd-cli {build} build ended with exit code {exitCode.ToString(CultureInfo.InvariantCulture)} (0x{exitCode.ToString("X8", CultureInfo.InvariantCulture)})"
                + (output.Length > 0 ? ": " + output : ", without any output. If it keeps failing, choose another Image acceleration under Settings → General.");
        return $"{modelName} could not make the image: {reason}";
    }

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

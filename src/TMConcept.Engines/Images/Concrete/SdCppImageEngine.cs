using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Images;
using TMConcept.Core.Models;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Images;

/// <summary>
/// The local image models (<c>local/…</c>): one stable-diffusion.cpp (<c>sd-cli</c>) run per image, on the graphics card, with the
/// prototype's arguments (<c>--diffusion-model</c>, <c>--llm</c>, <c>--vae</c>, euler, cfg 1, its steps and memory flags) and a random seed.
/// The picture is made at most 1024 pixels on its long side (a multiple of 64) and scaled by the engine. Free: the cost is 0. The pipeline
/// runs one video at a time, so the graphics card does one job at a time.
/// </summary>
public sealed class SdCppImageEngine(ToolInstaller tools, LocalImageStore store, IProcessRunner runner, ILogger<SdCppImageEngine> logger) : IImageEngine
{
    /// <summary>The long side of a generated picture.</summary>
    public const int LongSide = 1024;

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
            throw new InvalidOperationException($"{model.Name} is not installed. Choose it in Settings → Models and save: it is installed then.");
        }

        var cli = tools.Require(ToolKind.ImageRuntime);
        var (width, height) = Size(request.Width, request.Height);
        Directory.CreateDirectory(Path.GetDirectoryName(request.Output)!);
        var arguments = Arguments(model, request.Prompt, width, height, Random.Shared.NextInt64(1, int.MaxValue), request.Output, f => store.File(model, f));
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        var folder = Path.GetDirectoryName(cli)!;
        if (tools.Archive(ToolKind.ImageRuntimeCuda) is not null && tools.Status(ToolKind.ImageRuntimeCuda) is { Ready: true } cuda)
        {
            // The CUDA runtime libraries live in their own folder: Windows finds them through the PATH.
            environment["PATH"] = Path.GetDirectoryName(cuda.ExecutablePath) + Path.PathSeparator + folder + Path.PathSeparator
                + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        }
        else
        {
            environment["LD_LIBRARY_PATH"] = folder;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunAsync(new ProcessStart(cli, arguments, Path.GetDirectoryName(request.Output)!) { Environment = environment }, null,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(request.Output))
        {
            logger.LogError("{Model}: sd-cli ended with code {Code} after {Seconds:0.0} s", model.Id, result.ExitCode, watch.Elapsed.TotalSeconds);
            throw new InvalidOperationException($"{model.Name} could not make the image (sd-cli {result.ExitCode}): {ChildOutput.Summary(result.LastLines)}");
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

    /// <summary>The sd-cli arguments (the prototype's order); <paramref name="file"/> gives the path of a weights file.</summary>
    public static IReadOnlyList<string> Arguments(LocalImageModel model, string prompt, int width, int height, long seed, string output,
        Func<ToolArchive, string> file)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(file);
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
            "-t", Math.Clamp(Environment.ProcessorCount - 2, 1, 16).ToString(CultureInfo.InvariantCulture),
        };
        arguments.AddRange(model.Flags);
        return arguments;
    }
}

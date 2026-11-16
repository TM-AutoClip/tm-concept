using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;
using TMConcept.Core.Processes;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// Linux, after sysfs: NVIDIA cards with their memory from <c>nvidia-smi</c> (installed with NVIDIA's driver; sysfs has no memory file for
/// them). Asked only when a card's memory is still unknown; at most 10 seconds. Nothing when the tool is not on the PATH. As a memory source it
/// reads <c>memory.free</c> of the NVIDIA card with the same name (or the only one), at most 10 seconds per reading.
/// </summary>
public sealed class NvidiaSmiAdapterSource(ToolInstaller tools, IProcessRunner runner, ILogger<NvidiaSmiAdapterSource> logger) : IGraphicsAdapterSource, IVideoMemorySource
{
    /// <inheritdoc />
    public string Name => "nvidia-smi";

    /// <inheritdoc />
    public IReadOnlyList<GpuAdapter> Read()
    {
        if (tools.FindProgram("nvidia-smi") is not { } smi)
        {
            return [];
        }

        var lines = new List<string>();
        try
        {
            // Called once per run from a synchronous property (the image build is chosen by Ready); the runner reads the output on the thread
            // pool, so waiting here cannot deadlock.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = runner.RunAsync(new ProcessStart(smi, ["--query-gpu=name,memory.total", "--format=csv,noheader,nounits"], Path.GetDirectoryName(smi)!),
                lines.Add, timeout.Token).GetAwaiter().GetResult();
            if (result.ExitCode != 0)
            {
                logger.LogInformation("nvidia-smi ended with code {Code}", result.ExitCode);
                return [];
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
        {
            logger.LogWarning(ex, "nvidia-smi could not be run");
            return [];
        }

        return Parse(lines);
    }

    /// <inheritdoc />
    public async Task<long?> FreeBytesAsync(GpuAdapter adapter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapter.Vendor != GpuVendor.Nvidia || tools.FindProgram("nvidia-smi") is not { } smi)
        {
            return null;
        }

        var lines = new List<string>();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var result = await runner.RunAsync(new ProcessStart(smi, ["--query-gpu=name,memory.free", "--format=csv,noheader,nounits"], Path.GetDirectoryName(smi)!),
                lines.Add, timeout.Token).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning(ex, "nvidia-smi could not read the free graphics card memory");
            return null;
        }

        var cards = Parse(lines);
        var match = cards.FirstOrDefault(c => string.Equals(c.Name, adapter.Name, StringComparison.OrdinalIgnoreCase)) ?? (cards.Count == 1 ? cards[0] : null);
        return match?.VideoMemoryBytes;
    }

    /// <summary>The cards of nvidia-smi's <c>name, memory.total</c> lines (MiB, no units): <c>NVIDIA GeForce RTX 3060, 12288</c>.</summary>
    public static IReadOnlyList<GpuAdapter> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var list = new List<GpuAdapter>();
        foreach (var line in lines)
        {
            var comma = line.LastIndexOf(',');
            if (comma <= 0 || !long.TryParse(line[(comma + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var mebibytes) || mebibytes <= 0)
            {
                continue;
            }

            list.Add(new GpuAdapter(GpuVendor.Nvidia, line[..comma].Trim(), mebibytes * 1024 * 1024));
        }

        return list;
    }
}

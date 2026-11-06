using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;
using TMConcept.Core.Processes;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Hardware;

/// <summary>
/// The graphics card memory from <c>nvidia-smi</c> (installed with the NVIDIA driver, on Windows and Linux). Asked once per run, only when a
/// local image model is shown; null on a computer without it (another card or no driver): the page then says the memory is unknown.
/// </summary>
public sealed class NvidiaGpuInfo(ToolInstaller tools, IProcessRunner runner, ILogger<NvidiaGpuInfo> logger) : IGpuInfo
{
    private Task<long?>? _memory;

    /// <inheritdoc />
    public Task<long?> VideoMemoryBytesAsync(CancellationToken cancellationToken) => _memory ??= ReadAsync(cancellationToken);

    private async Task<long?> ReadAsync(CancellationToken cancellationToken)
    {
        if (tools.FindProgram("nvidia-smi") is not { } smi)
        {
            logger.LogInformation("Graphics card memory unknown: nvidia-smi is not on the PATH");
            return null;
        }

        var lines = new List<string>();
        try
        {
            var result = await runner.RunAsync(new ProcessStart(smi, ["--query-gpu=memory.total", "--format=csv,noheader,nounits"], Path.GetDirectoryName(smi)!),
                lines.Add, cancellationToken).ConfigureAwait(false);
            var mebibytes = lines.Select(l => long.TryParse(l.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0).DefaultIfEmpty(0).Max();
            if (result.ExitCode != 0 || mebibytes <= 0)
            {
                logger.LogInformation("Graphics card memory unknown: nvidia-smi ended with code {Code}", result.ExitCode);
                return null;
            }

            logger.LogInformation("Graphics card memory: {Mebibytes} MiB", mebibytes);
            return mebibytes * 1024 * 1024;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Graphics card memory could not be read");
            return null;
        }
    }
}

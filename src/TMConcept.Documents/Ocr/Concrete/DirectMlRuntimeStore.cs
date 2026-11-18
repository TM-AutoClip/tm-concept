using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The DirectML runtime of the local OCR as installed on this computer (0.1.27): <c>&lt;tools&gt;/ocr/directml/&lt;version&gt;/</c> with the
/// files of <see cref="DirectMlRuntimeRelease"/> (a file is there only after its SHA-256 matched). Offered only on Windows x64 with a graphics
/// card; downloaded only after the user agreed.
/// </summary>
public sealed class DirectMlRuntimeStore(IAppPaths paths, IToolPlatform platform, IGpuInfo gpu, IZipEntryDownloader downloader,
    ILogger<DirectMlRuntimeStore> logger)
{
    /// <summary>The runtime id that has a DirectML build.</summary>
    public const string Rid = "win-x64";

    /// <summary>The install folder.</summary>
    public string Folder => Path.Combine(paths.ToolsDirectory, "ocr", "directml", DirectMlRuntimeRelease.Version);

    /// <summary>Whether this computer can use it: Windows x64 with a graphics card (looked at once per run).</summary>
    public bool Supported => platform.Rid == Rid && gpu.Detect().Adapters.Count > 0;

    /// <summary>Files that are not there yet.</summary>
    public IReadOnlyList<ZipEntryArchive> Missing => DirectMlRuntimeRelease.Files.Where(f => !File.Exists(Path.Combine(Folder, f.FileName))).ToList();

    /// <summary>Whether every file is there.</summary>
    public bool Ready => Missing.Count == 0;

    /// <summary>Downloads the missing files (network; only after the user agreed).</summary>
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = Missing;
        Directory.CreateDirectory(Folder);
        for (var i = 0; i < missing.Count; i++)
        {
            var index = i;
            var inner = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((index + p.Share) / missing.Count, p.Text)));
            await downloader.DownloadAsync(missing[i], Path.Combine(Folder, missing[i].FileName), inner, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Text recognition on the graphics card: {File} downloaded and verified", missing[i].FileName);
        }
    }
}

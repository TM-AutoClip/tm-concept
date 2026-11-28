using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>Downloads a pinned voice model (SHA-256 checked before unpacking) into <c>&lt;tools&gt;/speech/&lt;name&gt;</c>, only after consent.</summary>
public sealed class SpeechModelInstaller(IAppPaths paths, IArchiveDownloader downloader, IToolPlatform platform, ILogger<SpeechModelInstaller> logger)
{
    private const string Marker = ".installed";

    /// <summary>The model folder (inside the archive's root).</summary>
    public string Folder(ToolArchive model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Path.Combine(paths.ToolsDirectory, "speech", model.Tool.ToString().ToLowerInvariant(), model.Root);
    }

    /// <summary>
    /// Where the model's archive is downloaded to (the shared downloads folder; deleted after unpacking, a <c>.part</c> next to it while it
    /// comes).
    /// </summary>
    public string DownloadFile(ToolArchive model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Path.Combine(paths.ToolsDirectory, ComponentFolders.DownloadsFolder, model.Url.Segments[^1]);
    }

    /// <summary>Whether the model is installed.</summary>
    public bool Ready(ToolArchive model) =>
        File.Exists(Path.Combine(Folder(model), model.ExecutablePath)) && File.Exists(Path.Combine(Path.GetDirectoryName(Folder(model))!, Marker));

    /// <summary>Installs.</summary>
    public async Task InstallAsync(ToolArchive model, bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!agreed)
        {
            throw new InvalidOperationException("A voice model is installed only after the user agreed.");
        }

        var target = Path.GetDirectoryName(Folder(model))!;
        var unpack = target + ".unpack";
        var file = DownloadFile(model);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            await downloader.DownloadAsync(model, file, progress, cancellationToken).ConfigureAwait(false);
            Delete(unpack);
            Directory.CreateDirectory(unpack);
            await platform.ExtractAsync(file, unpack, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(Path.Combine(unpack, model.Root, model.ExecutablePath)))
            {
                throw new ToolInstallException($"The voice archive has no {model.ExecutablePath}.") { Code = "extract" };
            }

            foreach (var unused in SpeechModels.Unused)
            {
                Delete(Path.Combine(unpack, model.Root, unused));
            }

            Delete(target);
            Directory.Move(unpack, target);
            await File.WriteAllTextAsync(Path.Combine(target, Marker), model.Sha256, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Voice model {Model} installed", model.Root);
        }
        finally
        {
            Delete(unpack);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private static void Delete(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

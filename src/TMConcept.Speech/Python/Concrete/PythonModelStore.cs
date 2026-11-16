using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Python;

/// <summary>
/// The model files of the Python voices: downloaded one by one into <c>&lt;tools&gt;/speech-py/models/&lt;code&gt;</c> (a file is there only after
/// its SHA-256 matched; a broken download is deleted), only when the user confirmed the download. The worker reads them from there, offline.
/// </summary>
public sealed class PythonModelStore(PythonLayout layout, IArchiveDownloader downloader, ILogger<PythonModelStore> logger)
{
    /// <summary>The model folder of a voice.</summary>
    public string Folder(PythonVoice voice)
    {
        ArgumentNullException.ThrowIfNull(voice);
        return layout.ModelFolder(voice.Code);
    }

    /// <summary>The files of a voice that are not there yet.</summary>
    public IReadOnlyList<ToolArchive> Missing(PythonVoice voice) =>
        voice.Files.Where(f => !File.Exists(Path.Combine(Folder(voice), f.ExecutablePath))).ToList();

    /// <summary>Whether every file of a voice is there.</summary>
    public bool Installed(PythonVoice voice) => Missing(voice).Count == 0;

    /// <summary>Downloads the missing files of a voice (only after the user agreed).</summary>
    public async Task InstallAsync(PythonVoice voice, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = Missing(voice);
        var total = Math.Max(1, missing.Sum(f => f.ApproximateBytes));
        var done = 0L;
        foreach (var file in missing)
        {
            var target = Path.Combine(Folder(voice), file.ExecutablePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var before = done;
            var inner = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((before + p.Share * file.ApproximateBytes) / total, p.Text)));
            await downloader.DownloadAsync(file, target, inner, cancellationToken).ConfigureAwait(false);
            done += file.ApproximateBytes;
            logger.LogInformation("{Voice}: {File} downloaded and verified", voice.Name, file.ExecutablePath);
        }
    }
}

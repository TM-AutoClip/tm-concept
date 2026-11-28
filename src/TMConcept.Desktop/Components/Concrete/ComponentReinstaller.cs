using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Components;

/// <summary>
/// The components after a new app version, or on request (General settings). What "was installed" comes from the install record
/// (<see cref="IInstallRecord"/>, kept outside the components folder, with the app version of each install) and from the folders that hold
/// an install; an empty folder or a partial download does not count.
/// <list type="bullet">
/// <item><see cref="UpdateAsync"/> installs again, through the one install flow (<see cref="ComponentInstallFlow"/>: one confirmation, the
/// install window), what was installed before and is now missing or changed; each of them loses its old files for good right before its
/// download (<see cref="ComponentPlanner"/>), nothing else is touched.</item>
/// <item><see cref="ReinstallAsync"/> (28 Nov 2026, the developer: "sıfırdan kur ve güncellerde modelleri silecek önce, kalıcı tekte silecek,
/// geri dönüşüm değil, sonra indirecek"): ends the idle speech workers and the app's own Ollama, writes what was installed into the install
/// journal (<see cref="IInstallJournal"/>), deletes the whole components folder (<c>&lt;data&gt;/tools</c>) for good (no recycle bin, no
/// folder set aside, no second copy: until 0.1.56 the old folder was renamed aside while the new one was installed and the disk held both),
/// and only then installs what was there. A declined, cancelled, failed or cut-short install leaves the rest in the journal: the next start
/// deletes the half-finished one's files and downloads what is missing again; the finished components stay.</item>
/// </list>
/// The database, the key file, the profiles, the concepts and the videos are outside that folder and never touched.
/// </summary>
public sealed class ComponentReinstaller(ComponentPlanner planner, ComponentInstallFlow flow, Core.DiskSpace.IFolderDiscard discard, AppPaths paths,
    Speech.Speech.SpeechWorkerClient speech, Speech.Python.PythonWorkerClient python, IActivityLog activity, Localizer text, IComponentWorkGate gate,
    IInstallJournal journal, ILogger<ComponentReinstaller> logger, Core.AppOllama.IAppOllamaServer? ollama = null)
{
    /// <summary>The text of what an install confirmation says a cancel means here.</summary>
    public const string PurposeKey = "install.purpose.update";

    /// <summary>
    /// Where the components live, as the user knows the folder: <c>%LOCALAPPDATA%\TM Concept\tools</c> on Windows,
    /// <c>~/.local/share/tm-concept/tools</c> on Linux (a portable data folder as it is).
    /// </summary>
    public string Location() => Location(paths.ToolsDirectory);

    /// <summary>The components folder (its size goes into the reinstall confirmation).</summary>
    public string Folder => paths.ToolsDirectory;

    /// <summary>A folder as the user knows it (<see cref="Location()"/>).</summary>
    private static string Location(string tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (local.Length > 0 && tools.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "%LOCALAPPDATA%" + tools[local.Length..];
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && tools.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "~" + tools[home.Length..] : tools;
    }

    /// <summary>
    /// The codes of the components that were installed (this version's or an earlier one's): the install record's and those on disk, after
    /// looking at the disk again.
    /// </summary>
    public Task<IReadOnlyList<string>> InstalledAsync(CancellationToken cancellationToken) => planner.InstalledAsync(cancellationToken);

    /// <summary>Installs again what was installed before and is now missing or changed (one confirmation lists the downloads).</summary>
    public async Task<InstallOutcome> UpdateAsync(CancellationToken cancellationToken)
    {
        var codes = await InstalledAsync(cancellationToken).ConfigureAwait(true);
        logger.LogInformation("Components update: {Count} were installed before ({Codes})", codes.Count, string.Join(", ", codes));
        return codes.Count == 0 ? new InstallOutcome(true, string.Empty) : await flow.EnsureAsync(codes, PurposeKey, null, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Deletes the whole components folder for good, then installs again what was installed (one confirmation lists the downloads). What is
    /// not installed in the end stays in the install journal and is downloaded again from the next start.
    /// </summary>
    public async Task<InstallOutcome> ReinstallAsync(CancellationToken cancellationToken)
    {
        var codes = await InstalledAsync(cancellationToken).ConfigureAwait(true);
        var tools = paths.ToolsDirectory;
        logger.LogInformation("Components reinstall: {Count} were installed ({Codes})", codes.Count, string.Join(", ", codes));
        if (!await Task.Run(() => speech.StopIfIdle() & python.StopIfIdle(), CancellationToken.None).ConfigureAwait(true))
        {
            logger.LogInformation("Components reinstall: the speech worker is speaking; nothing was changed");
            return new InstallOutcome(false, text["components.reinstall.busy"]);
        }

        // 0.1.27: a library this process loaded from the folder (PDFium, the OCR's DirectML runtime) stays loaded until the app ends, so its
        // files cannot be deleted: the user is told to restart first (nothing is deleted, so nothing is half gone).
        if (planner.All.Where(c => c.LoadedInProcess).Select(c => c.Name).ToList() is { Count: > 0 } loaded)
        {
            logger.LogInformation("Components reinstall: {Components} loaded by this process; nothing was changed", string.Join(", ", loaded));
            var reason = text.Format("components.loaded_restart", string.Join(", ", loaded));
            activity.Add(ActivityLevel.Warning, reason);
            return new InstallOutcome(false, reason);
        }

        // While this runs the start-up sweep and the storage page leave the components folder alone.
        using var hold = gate.Install();

        // Written before the delete: from here on a close in the middle (a crash, a power cut) is finished at the next start.
        journal.Begin(codes);
        if (Directory.Exists(tools))
        {
            var size = await Task.Run(() => Core.DiskSpace.DiskSizes.Of(tools), CancellationToken.None).ConfigureAwait(true);
            try
            {
                await StopOllamaAsync().ConfigureAwait(true);
                await discard.DiscardAsync(tools, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A file in use (a program from the folder still running, a virus scanner): what was deleted stays deleted; the journal
                // keeps every component, so the next start deletes what is left of them and downloads them again.
                logger.LogError(LogEvents.OnScreen, ex, "Components reinstall: the components folder could not be deleted completely");
                var line = text.Format("components.reinstall.delete_failed", ex.Message, Location());
                activity.Add(ActivityLevel.Error, line);
                return new InstallOutcome(false, line);
            }

            logger.LogInformation("Components reinstall: the components folder ({Bytes} bytes) was deleted permanently; installing {Count} component(s)", size,
                codes.Count);
            activity.Add(ActivityLevel.Info, text.Format("components.reinstall.deleted", Localization.ByteSizeText.Of(size, text.Culture)));
        }

        InstallOutcome outcome;
        try
        {
            await planner.RefreshAllAsync(cancellationToken).ConfigureAwait(true);
            outcome = codes.Count == 0 ? new InstallOutcome(true, string.Empty) : await flow.EnsureAsync(codes, PurposeKey, null, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Components reinstall: the install stopped");
            outcome = new InstallOutcome(false, text.Format("install.failed", ex.Message));
        }

        if (outcome.Ready)
        {
            activity.Add(ActivityLevel.Info, text["components.reinstall.done"]);
            return outcome;
        }

        logger.LogInformation("Components reinstall did not finish ({Reason}); what is missing is downloaded again from the next start", outcome.Reason);
        return outcome with { Reason = Join(outcome.Reason, text["components.reinstall.resume"]) };
    }

    /// <summary>Ends the app's own Ollama (it runs from the components folder; a running server keeps its files from being deleted).</summary>
    private async Task StopOllamaAsync()
    {
        if (ollama is not { Running: true })
        {
            return;
        }

        try
        {
            await ollama.StopAsync(CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("Components reinstall: the app's Ollama was stopped before the components folder is deleted");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Components reinstall: the app's Ollama could not be stopped");
        }
    }

    private static string Join(string first, string second) => first.Length == 0 ? second : first + " " + second;
}

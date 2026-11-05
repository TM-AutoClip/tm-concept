using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Engines;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Tools;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Tools: Node.js, ffmpeg and the video engines (their npm packages and Chrome). Nothing is downloaded without a confirmation that names
/// the source and size; Remotion first asks for its license. A ready engine can render a sample (no model, no network). On Linux missing
/// Chrome libraries are listed with the apt command to run (the app never uses root).
/// </summary>
public sealed partial class ToolsSettingsViewModel(ToolInstaller installer, IToolPlatform platform, IEnumerable<IVideoEngine> engines,
    IEnumerable<Core.Speech.ISpeechEngine> voices, SampleRender sample,
    AppSession session, IConfirmDialog confirm, ShellLauncher shell, IActivityLog activity, Localizer text, ILogger<ToolsSettingsViewModel> logger)
    : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.tools";

    /// <inheritdoc />
    public override int Order => 40;

    /// <summary>Rows.</summary>
    public ObservableCollection<ToolRowViewModel> Rows { get; } = [];

    /// <summary>apt command for missing libraries (Linux).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingLibraries))]
    public partial string MissingLibraries { get; set; } = string.Empty;

    /// <summary>Whether libraries are missing.</summary>
    public bool HasMissingLibraries => MissingLibraries.Length > 0;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        Rows.Clear();
        Rows.Add(new ToolRowViewModel("node", "Node.js", text["tools.node.purpose"]));
        Rows.Add(new ToolRowViewModel("ffmpeg", "ffmpeg", text["tools.ffmpeg.purpose"]));
        foreach (var engine in engines.OrderBy(e => e.Kind))
        {
            var note = engine.Experimental ? text["engines.experimental"] : engine.NeedsLicenseConsent ? text["engines.license_short"] : text["engines.default"];
            Rows.Add(new ToolRowViewModel(engine.Kind.ToString(), engine.Name, note, engine));
        }

        foreach (var voice in voices.Where(v => v.IsLocal))
        {
            var note = voice.Code == Core.Speech.SpeechEngines.PiperTurkish ? text["speech.piper.note"] : text["speech.supertonic.note"];
            Rows.Add(new ToolRowViewModel(voice.Code, voice.Name, note, voice: voice));
        }

        foreach (var row in Rows)
        {
            Refresh(row);
        }

        var missing = await platform.MissingLibrariesAsync(CancellationToken.None).ConfigureAwait(true);
        MissingLibraries = missing.Count == 0 ? string.Empty : "sudo apt install " + string.Join(' ', missing);
    }

    /// <summary>Installs a tool or an engine after a confirmation.</summary>
    [RelayCommand]
    public async Task InstallAsync(ToolRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.Engine is { } engine)
        {
            await InstallEngineAsync(row, engine).ConfigureAwait(true);
            return;
        }

        if (row.Voice is { } voice)
        {
            await InstallVoiceAsync(row, voice).ConfigureAwait(true);
            return;
        }

        if (!Enum.TryParse<ToolKind>(row.Code, ignoreCase: true, out var tool) || installer.Archive(tool) is not { } archive)
        {
            return;
        }

        var size = (archive.ApproximateBytes / 1_000_000.0).ToString("0", CultureInfo.InvariantCulture);
        var answer = await confirm.AskAsync(text["tools.confirm.title"], text.Format("tools.confirm.message", row.Name, archive.Version, archive.Url.Host, size),
            text["tools.install"], danger: false).ConfigureAwait(true);
        if (answer.Confirmed)
        {
            await RunAsync(row, progress => installer.InstallAsync(tool, agreed: true, progress, CancellationToken.None),
                text.Format("tools.installed", row.Name, archive.Version)).ConfigureAwait(true);
        }
    }

    /// <summary>Renders the engine's sample (install check; no model, no network).</summary>
    [RelayCommand]
    public async Task SampleAsync(ToolRowViewModel? row)
    {
        if (row?.Engine is not { } engine)
        {
            return;
        }

        row.Busy = true;
        row.Percent = 0;
        try
        {
            var progress = new Progress<EngineEvent>(e => row.Percent = (e.Ratio ?? 0) * 100);
            var video = await sample.RenderAsync(engine, progress, CancellationToken.None).ConfigureAwait(true);
            activity.Add(ActivityLevel.Info, text.Format("engines.sample_done", engine.Name));
            shell.OpenPath(Path.GetDirectoryName(video)!);
        }
        catch (EngineException ex)
        {
            logger.LogError(ex, "Sample render of {Engine} failed ({Code})", engine.Name, ex.Code);
            activity.Add(ActivityLevel.Error, text.Format("engines.sample_failed", engine.Name, ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ToolInstallException or InvalidOperationException)
        {
            logger.LogError(ex, "Sample render of {Engine} failed", engine.Name);
            activity.Add(ActivityLevel.Error, text.Format("engines.sample_failed", engine.Name, ex.Message));
        }
        finally
        {
            row.Busy = false;
            Refresh(row);
        }
    }

    private async Task InstallEngineAsync(ToolRowViewModel row, IVideoEngine engine)
    {
        if (!installer.Status(ToolKind.Node).Ready)
        {
            activity.Add(ActivityLevel.Warning, text["engines.node_first"]);
            return;
        }

        if (engine.NeedsLicenseConsent && !session.Settings.RemotionLicenseAccepted)
        {
            var license = await confirm.AskAsync(text["engines.license.title"], text["engines.license.message"], text["engines.license.accept"], danger: false)
                .ConfigureAwait(true);
            if (!license.Confirmed)
            {
                return;
            }

            session.Settings.RemotionLicenseAccepted = true;
            await session.SaveAsync(session.Settings, CancellationToken.None).ConfigureAwait(true);
        }

        var answer = await confirm.AskAsync(text["tools.confirm.title"], text.Format("engines.confirm.message", engine.Name), text["tools.install"], danger: false)
            .ConfigureAwait(true);
        if (answer.Confirmed)
        {
            await RunAsync(row, progress => engine.InstallAsync(agreed: true, progress, CancellationToken.None), text.Format("tools.installed", engine.Name, string.Empty))
                .ConfigureAwait(true);
            foreach (var other in Rows.Where(r => r.Engine is not null))
            {
                Refresh(other);
            }
        }
    }

    private async Task InstallVoiceAsync(ToolRowViewModel row, Core.Speech.ISpeechEngine voice)
    {
        var piper = voice.Code == Core.Speech.SpeechEngines.PiperTurkish;
        var accepted = piper ? session.Settings.PiperNonCommercialAccepted : session.Settings.SupertonicLicenseAccepted;
        if (!accepted)
        {
            var license = await confirm.AskAsync(text[piper ? "speech.piper.license.title" : "speech.supertonic.license.title"],
                text[piper ? "speech.piper.license.message" : "speech.supertonic.license.message"], text["engines.license.accept"], danger: piper)
                .ConfigureAwait(true);
            if (!license.Confirmed)
            {
                return;
            }

            if (piper)
            {
                session.Settings.PiperNonCommercialAccepted = true;
            }
            else
            {
                session.Settings.SupertonicLicenseAccepted = true;
            }

            await session.SaveAsync(session.Settings, CancellationToken.None).ConfigureAwait(true);
        }

        var model = piper ? Speech.Speech.SpeechModels.PiperTurkish : Speech.Speech.SpeechModels.Supertonic;
        var size = (model.ApproximateBytes / 1_000_000.0).ToString("0", CultureInfo.InvariantCulture);
        var answer = await confirm.AskAsync(text["tools.confirm.title"], text.Format("tools.confirm.message", voice.Name, model.Version, model.Url.Host, size),
            text["tools.install"], danger: false).ConfigureAwait(true);
        if (answer.Confirmed)
        {
            await RunAsync(row, progress => voice.InstallAsync(agreed: true, progress, CancellationToken.None), text.Format("tools.installed", voice.Name, model.Version))
                .ConfigureAwait(true);
        }
    }

    private async Task RunAsync(ToolRowViewModel row, Func<IProgress<ToolProgress>, Task> work, string done)
    {
        row.Busy = true;
        row.Percent = 0;
        try
        {
            await work(new Progress<ToolProgress>(p => row.Percent = p.Share * 100)).ConfigureAwait(true);
            activity.Add(ActivityLevel.Info, done.Trim());
        }
        catch (ToolInstallException ex)
        {
            logger.LogError(ex, "{Tool} could not be installed ({Code})", row.Name, ex.Code);
            activity.Add(ActivityLevel.Error, text.Format("tools.failed", row.Name, text["tools.fail." + ex.Code]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
        {
            logger.LogError(ex, "{Tool} could not be installed", row.Name);
            activity.Add(ActivityLevel.Error, text.Format("tools.failed", row.Name, ex.Message));
        }
        finally
        {
            row.Busy = false;
            Refresh(row);
        }
    }

    private void Refresh(ToolRowViewModel row)
    {
        if (row.Voice is { } voice)
        {
            row.Ready = voice.Ready;
            row.State = voice.Ready ? "✓ " + text["engines.ready"] : text["tools.state.missing"];
            return;
        }

        if (row.Engine is { } engine)
        {
            var status = engine.Status();
            row.Ready = status.Ready;
            row.State = status.Ready ? "✓ " + text["engines.ready"] : text.Format("engines.missing", string.Join(", ", status.Missing.Select(m => text["engines.part." + m])));
            return;
        }

        var tool = Enum.Parse<ToolKind>(row.Code, ignoreCase: true);
        var tools = installer.Status(tool);
        row.Ready = tools.Ready;
        row.State = tools.Ready
            ? "✓ " + (tools.Source == "system" ? text["tools.state.system"] : text.Format("tools.state.ready", tools.Version))
            : installer.Archive(tool) is null ? text["tools.state.unsupported"] : text["tools.state.missing"];
    }
}

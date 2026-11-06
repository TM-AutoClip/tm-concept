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
/// Tools: Node.js, ffmpeg, the speech libraries, the video engines and the local voice (their npm packages and Chrome). Nothing is downloaded without a confirmation that names
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
        Rows.Add(new ToolRowViewModel(nameof(ToolKind.SpeechRuntime), "sherpa-onnx", text["tools.speechruntime.purpose"]));
        foreach (var engine in engines.OrderBy(e => e.Kind))
        {
            var note = engine.Experimental ? text["engines.experimental"] : engine.NeedsLicenseConsent ? text["engines.license_short"] : text["engines.default"];
            Rows.Add(new ToolRowViewModel(engine.Kind.ToString(), engine.Name, note, engine));
        }

        foreach (var voice in voices.Where(v => v.IsLocal))
        {
            Rows.Add(new ToolRowViewModel(voice.Code, voice.Name, text["speech.supertonic.note"], voice: voice));
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

        var answer = await confirm.AskAsync(text["tools.confirm.title"],
            text.Format("tools.confirm.message", row.Name, archive.Version, archive.Url.Host, Megabytes(archive.ApproximateBytes)),
            text["tools.install"], danger: false).ConfigureAwait(true);
        if (answer.Confirmed)
        {
            await RunAsync(row, async progress =>
            {
                var status = await installer.InstallAsync(tool, agreed: true, progress, CancellationToken.None).ConfigureAwait(true);
                return status.Source == ToolInstaller.FallbackSource
                    ? text.Format("tools.fallback", row.Name)
                    : text.Format("tools.installed", row.Name, archive.Version);
            }).ConfigureAwait(true);
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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            logger.LogInformation("Sample render of {Engine} started", engine.Name);
            var video = await sample.RenderAsync(engine, progress, CancellationToken.None).ConfigureAwait(true);
            logger.LogInformation("Sample render of {Engine} finished after {Seconds:0.0} s", engine.Name, watch.Elapsed.TotalSeconds);
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
            await RunAsync(row, async progress =>
            {
                await engine.InstallAsync(agreed: true, progress, CancellationToken.None).ConfigureAwait(true);
                return text.Format("tools.installed", engine.Name, string.Empty);
            })
                .ConfigureAwait(true);
            foreach (var other in Rows.Where(r => r.Engine is not null))
            {
                Refresh(other);
            }
        }
    }

    private async Task InstallVoiceAsync(ToolRowViewModel row, Core.Speech.ISpeechEngine voice)
    {
        if (!session.Settings.SupertonicLicenseAccepted)
        {
            var license = await confirm.AskAsync(text["speech.supertonic.license.title"], text["speech.supertonic.license.message"], text["engines.license.accept"],
                danger: false).ConfigureAwait(true);
            if (!license.Confirmed)
            {
                return;
            }

            session.Settings.SupertonicLicenseAccepted = true;
            await session.SaveAsync(session.Settings, CancellationToken.None).ConfigureAwait(true);
        }

        // The voice needs the sherpa-onnx libraries too (a tool of their own): one confirmation names what is missing. A model that is
        // already there (an update from 0.1.2, where the libraries were inside the app) is not downloaded again.
        var model = Speech.Speech.SpeechModels.Supertonic;
        var needModel = voice is not Speech.Speech.SherpaSpeechEngineBase { ModelInstalled: true };
        var runtime = installer.Status(ToolKind.SpeechRuntime).Ready ? null : installer.Archive(ToolKind.SpeechRuntime);
        if (!needModel && runtime is null)
        {
            return;
        }

        var message = runtime is null
            ? text.Format("tools.confirm.message", voice.Name, model.Version, model.Url.Host, Megabytes(model.ApproximateBytes))
            : !needModel
                ? text.Format("tools.confirm.message", "sherpa-onnx", runtime.Version, runtime.Url.Host, Megabytes(runtime.ApproximateBytes))
                : text.Format("tools.confirm.voice", voice.Name, model.Version, model.Url.Host, Megabytes(model.ApproximateBytes), runtime.Version,
                    Megabytes(runtime.ApproximateBytes));
        var answer = await confirm.AskAsync(text["tools.confirm.title"], message, text["tools.install"], danger: false).ConfigureAwait(true);
        if (answer.Confirmed)
        {
            await RunAsync(row, async progress =>
            {
                if (runtime is not null)
                {
                    await installer.InstallAsync(ToolKind.SpeechRuntime, agreed: true, progress, CancellationToken.None).ConfigureAwait(true);
                }

                if (needModel)
                {
                    await voice.InstallAsync(agreed: true, progress, CancellationToken.None).ConfigureAwait(true);
                }

                return text.Format("tools.installed", voice.Name, model.Version);
            }).ConfigureAwait(true);
            foreach (var other in Rows.Where(r => r.Code == nameof(ToolKind.SpeechRuntime)))
            {
                Refresh(other);
            }
        }
    }

    /// <summary>Runs an install; <paramref name="work"/> returns the line for the log (a fallback to the system's copy is a warning).</summary>
    private async Task RunAsync(ToolRowViewModel row, Func<IProgress<ToolProgress>, Task<string>> work)
    {
        row.Busy = true;
        row.Percent = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        logger.LogInformation("Installing {Tool}", row.Name);
        try
        {
            var done = await work(new Progress<ToolProgress>(p => row.Percent = p.Share * 100)).ConfigureAwait(true);
            logger.LogInformation("{Tool} install finished after {Seconds:0.0} s", row.Name, watch.Elapsed.TotalSeconds);
            activity.Add(Enum.TryParse<ToolKind>(row.Code, ignoreCase: true, out var kind) && installer.Status(kind).Source == ToolInstaller.FallbackSource
                ? ActivityLevel.Warning
                : ActivityLevel.Info, done.Trim());
        }
        catch (ToolInstallException ex)
        {
            logger.LogError(ex, "{Tool} could not be installed ({Code})", row.Name, ex.Code);
            activity.Add(ActivityLevel.Error, text.Format("tools.failed", row.Name, text.Format("tools.fail." + ex.Code, row.Name)));
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
            row.State = voice.Ready ? "✓ " + text["engines.ready"]
                : voice is Speech.Speech.SherpaSpeechEngineBase local
                    ? text.Format("tools.state.missing_size", Megabytes((local.ModelInstalled ? 0 : local.Model.ApproximateBytes)
                        + (installer.Status(ToolKind.SpeechRuntime).Ready ? 0 : installer.Archive(ToolKind.SpeechRuntime)?.ApproximateBytes ?? 0)))
                    : text["tools.state.missing"];
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
            ? "✓ " + (tools.Source == "system" ? text["tools.state.system"]
                : tools.Source == ToolInstaller.FallbackSource ? text["tools.state.fallback"]
                : text.Format("tools.state.ready", tools.Version))
            : installer.Archive(tool) is { } archive ? text.Format("tools.state.missing_size", Megabytes(archive.ApproximateBytes)) : text["tools.state.unsupported"];

        if (tool == ToolKind.SpeechRuntime)
        {
            foreach (var voiceRow in Rows.Where(r => r.Voice is not null))
            {
                Refresh(voiceRow);
            }
        }
    }

    private static string Megabytes(long bytes) => Math.Max(1, Math.Round(bytes / 1_000_000.0)).ToString("0", CultureInfo.InvariantCulture);
}

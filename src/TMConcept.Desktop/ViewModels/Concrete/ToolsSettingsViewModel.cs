using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Engines.Tools;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Tools: Node.js and ffmpeg (and, from the engines, their npm packages and Chrome). Nothing is downloaded without a confirmation that
/// names the source and size. On Linux missing Chrome libraries are listed with the apt command to run (the app never uses root).
/// </summary>
public sealed partial class ToolsSettingsViewModel(ToolInstaller installer, IToolPlatform platform, IConfirmDialog confirm, IActivityLog activity,
    Localizer text, ILogger<ToolsSettingsViewModel> logger) : SettingsSectionBase
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
        Rows.Add(Row(ToolKind.Node, "Node.js", text["tools.node.purpose"]));
        Rows.Add(Row(ToolKind.Ffmpeg, "ffmpeg", text["tools.ffmpeg.purpose"]));
        var missing = await platform.MissingLibrariesAsync(CancellationToken.None).ConfigureAwait(true);
        MissingLibraries = missing.Count == 0 ? string.Empty : "sudo apt install " + string.Join(' ', missing);
    }

    /// <summary>Installs a tool after a confirmation.</summary>
    [RelayCommand]
    public async Task InstallAsync(ToolRowViewModel? row)
    {
        if (row is null || !Enum.TryParse<ToolKind>(row.Code, ignoreCase: true, out var tool) || installer.Archive(tool) is not { } archive)
        {
            return;
        }

        var size = (archive.ApproximateBytes / 1_000_000.0).ToString("0", CultureInfo.InvariantCulture);
        var answer = await confirm.AskAsync(text["tools.confirm.title"], text.Format("tools.confirm.message", row.Name, archive.Version, archive.Url.Host, size),
            text["tools.install"], danger: false).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        row.Busy = true;
        try
        {
            var progress = new Progress<ToolProgress>(p => row.Percent = p.Share * 100);
            await installer.InstallAsync(tool, agreed: true, progress, CancellationToken.None).ConfigureAwait(true);
            activity.Add(ActivityLevel.Info, text.Format("tools.installed", row.Name, archive.Version));
        }
        catch (ToolInstallException ex)
        {
            logger.LogError(ex, "{Tool} could not be installed ({Code})", tool, ex.Code);
            activity.Add(ActivityLevel.Error, text.Format("tools.failed", row.Name, text["tools.fail." + ex.Code]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            logger.LogError(ex, "{Tool} could not be installed", tool);
            activity.Add(ActivityLevel.Error, text.Format("tools.failed", row.Name, ex.Message));
        }
        finally
        {
            row.Busy = false;
            Refresh(row, tool);
        }
    }

    private ToolRowViewModel Row(ToolKind tool, string name, string purpose)
    {
        var row = new ToolRowViewModel(tool.ToString().ToLowerInvariant(), name, purpose);
        Refresh(row, tool);
        return row;
    }

    private void Refresh(ToolRowViewModel row, ToolKind tool)
    {
        var status = installer.Status(tool);
        row.Ready = status.Ready;
        row.State = status.Ready
            ? "✓ " + (status.Source == "system" ? text["tools.state.system"] : text.Format("tools.state.ready", status.Version))
            : installer.Archive(tool) is null ? text["tools.state.unsupported"] : text["tools.state.missing"];
    }
}

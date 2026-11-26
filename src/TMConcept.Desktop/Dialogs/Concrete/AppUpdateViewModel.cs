using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// The window that offers a newer version: "A new update is available", the notes of every version between the running one and the newest
/// (oldest first, in the app's language), and "Download and install" of the newest (only when the app can install it
/// itself; otherwise why not and the release page). The download shows its progress and can be cancelled; a failure stays on screen with
/// its reason; on success the app restarts as the new version.
/// </summary>
public sealed partial class AppUpdateViewModel(UpdateOffer offer, AppUpdater updater, IShellLauncher shell, Localizer text, ILogger logger)
    : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cancel;
    private IReadOnlyList<ReleaseNoteRow>? _versions;

    /// <summary>Raised when the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The offer.</summary>
    public UpdateOffer Offer { get; } = offer;

    /// <summary>"A new update is available".</summary>
    public string Title => text["appupdate.title"];

    /// <summary>"You have 0.1.17; the newest is 0.1.20 (3 new versions)."</summary>
    public string Subtitle => Versions.Count > 1
        ? text.Format("appupdate.subtitle.many", Offer.Current, Offer.Version, Versions.Count)
        : text.Format("appupdate.subtitle", Offer.Current, Offer.Version);

    /// <summary>
    /// Every newer version, oldest first, with its notes in the app's language (no SHA-256 block, Markdown as plain text); just the newest
    /// when the offer names no others.
    /// </summary>
    public IReadOnlyList<ReleaseNoteRow> Versions => _versions ??= Rows();

    /// <summary>Whether the app can install it itself.</summary>
    public bool CanInstall => Offer.CanInstall;

    /// <summary>Why the app cannot install it (empty when it can).</summary>
    public string Problem => Offer.CanInstall ? string.Empty : text["appupdate.problem." + Offer.Problem];

    /// <summary>What happens on install (the size and the restart).</summary>
    public string Hint => Offer.Asset is { } asset
        ? text.Format(Offer.Installed is null ? "appupdate.hint" : "appupdate.hint.installed", asset.Name, ComponentInstallFlow.Megabytes(asset.Size))
        : string.Empty;

    /// <summary>Downloading or installing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Busy { get; set; }

    /// <summary>Not busy.</summary>
    public bool Idle => !Busy;

    /// <summary>The new version was installed: the app is closing to start it.</summary>
    public bool Installed { get; private set; }

    /// <summary>Download progress (0–100).</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>The state line (progress, failure).</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Downloads, checks and installs; the app restarts when it went through.</summary>
    [RelayCommand]
    public async Task InstallAsync()
    {
        if (Busy || !CanInstall)
        {
            return;
        }

        Busy = true;
        Status = text["appupdate.downloading"];
        var cancel = new CancellationTokenSource();
        _cancel = cancel;
        try
        {
            var progress = new Progress<ToolProgress>(p =>
            {
                Percent = Math.Round(p.Share * 100);
                Status = text.Format("appupdate.progress", Percent);
            });
            await updater.InstallAsync(Offer, progress, cancel.Token).ConfigureAwait(true);
            Installed = true;
            Status = text["appupdate.restarting"];
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            logger.LogInformation("The update download was cancelled; the part downloaded so far is kept until the next start");
            Status = text["appupdate.cancelled"];
        }
        catch (ToolInstallException ex)
        {
            logger.LogError(ex, "The update could not be downloaded");
            Status = text.Format("appupdate.failed", ComponentInstallFlow.Reason(text, ex));
        }
        catch (AppUpdateException ex)
        {
            logger.LogWarning(ex, "The update was not installed ({Code})", ex.Code);
            Status = text["appupdate.fail." + ex.Code];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError(ex, "The update could not be installed; the running version stays");
            Status = text.Format("appupdate.failed", ex.Message);
        }
        finally
        {
            Busy = false;
            cancel.Dispose();
            _cancel = null;
        }
    }

    /// <summary>Stops the download (the running version stays).</summary>
    [RelayCommand]
    public void Cancel() => _cancel?.Cancel();

    /// <summary>Opens the release page in the browser.</summary>
    [RelayCommand]
    public void OpenPage() => shell.Open(Offer.Page);

    /// <summary>Closes the window (asked again at a later start).</summary>
    [RelayCommand]
    public void Later()
    {
        _cancel?.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private List<ReleaseNoteRow> Rows()
    {
        var versions = Offer.Versions.Count > 0 ? Offer.Versions : [new VersionNotes(Offer.Version, Offer.Notes, null)];
        return versions.Select(v =>
        {
            var notes = ReleaseNotesText.For(v.Body, text.Language);
            var header = v.PublishedAt is { } at
                ? v.Version + " · " + at.ToLocalTime().ToString("d MMMM yyyy", text.Culture)
                : v.Version.ToString();
            return new ReleaseNoteRow(header, notes.Length > 0 ? notes : text["appupdate.no_notes"]);
        }).ToList();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // A download still running when the window goes away is stopped, not left to replace the program unseen (InstallAsync disposes its
        // own token source when it ends).
        _cancel?.Cancel();
    }
}

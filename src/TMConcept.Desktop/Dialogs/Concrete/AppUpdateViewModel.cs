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
/// The window that offers a newer version: the version, the release notes, and "Download and install" (only when the app can install it
/// itself; otherwise why not and the release page). The download shows its progress and can be cancelled; a failure stays on screen with
/// its reason; on success the app restarts as the new version.
/// </summary>
public sealed partial class AppUpdateViewModel(UpdateOffer offer, AppUpdater updater, IShellLauncher shell, Localizer text, ILogger logger)
    : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cancel;

    /// <summary>Raised when the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The offer.</summary>
    public UpdateOffer Offer { get; } = offer;

    /// <summary>"TM Concept 0.1.18 is available (you have 0.1.17)".</summary>
    public string Title => text.Format("appupdate.title", Offer.Version, Offer.Current);

    /// <summary>The release notes as published.</summary>
    public string Notes => Offer.Notes.Length > 0 ? Offer.Notes : text["appupdate.no_notes"];

    /// <summary>Whether the app can install it itself.</summary>
    public bool CanInstall => Offer.CanInstall;

    /// <summary>Why the app cannot install it (empty when it can).</summary>
    public string Problem => Offer.CanInstall ? string.Empty : text["appupdate.problem." + Offer.Problem];

    /// <summary>What happens on install (the size and the restart).</summary>
    public string Hint => Offer.Asset is { } asset ? text.Format("appupdate.hint", asset.Name, ComponentInstallFlow.Megabytes(asset.Size)) : string.Empty;

    /// <summary>Downloading or installing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    public partial bool Busy { get; set; }

    /// <summary>Not busy.</summary>
    public bool Idle => !Busy;

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

    /// <inheritdoc />
    public void Dispose()
    {
        // A download still running when the window goes away is stopped, not left to replace the program unseen (InstallAsync disposes its
        // own token source when it ends).
        _cancel?.Cancel();
    }
}

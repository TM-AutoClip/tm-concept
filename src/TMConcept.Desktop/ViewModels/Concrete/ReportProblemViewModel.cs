using System.ComponentModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Reports;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// "Report a problem": says plainly that nothing is sent automatically and what the zip holds. Prepare logs builds the zip
/// (<see cref="ProblemReport"/>) and shows it in the file manager; Report on GitHub opens the issue form in the browser with the version and
/// system filled in (<see cref="IssueLink"/>); Copy details copies <see cref="SystemReport"/>'s text.
/// </summary>
public sealed partial class ReportProblemViewModel(ProblemReport report, SystemReport system, IShellLauncher shell, WindowHost host, Localizer text,
    ILogger<ReportProblemViewModel> logger) : ObservableObject
{
    /// <summary>The line under the explanation (what happened last).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>The zip made in this window, if any.</summary>
    [ObservableProperty]
    public partial string ZipPath { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Status"/> has text.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>Builds the zip (off the interface thread) and shows it.</summary>
    [RelayCommand]
    public async Task PrepareLogsAsync()
    {
        string zip;
        try
        {
            zip = await Task.Run(() => report.CreateAsync(CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError(ex, "The problem report could not be written");
            Status = text.Format("report.failed", ex.Message);
            return;
        }

        ZipPath = zip;
        try
        {
            shell.Reveal(zip);
            Status = text.Format("report.prepared", Path.GetFileName(zip));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // ShellLauncher wrote the error; the zip is there all the same.
            Status = text.Format("report.prepared_hidden", Path.GetFileName(zip), ex.Message);
        }
    }

    /// <summary>Opens the issue form in the browser (nothing is sent; the user fills and submits it).</summary>
    [RelayCommand]
    public async Task ReportOnGitHubAsync()
    {
        var address = IssueLink.ForThisProgram();
        try
        {
            shell.Open(address);
            logger.LogInformation("The GitHub issue form was opened (logs zip prepared: {Prepared})", ZipPath.Length > 0);
            Status = text["report.github_opened"];
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            await CopyAsync(address.AbsoluteUri).ConfigureAwait(true);
            Status = text.Format("report.github_failed", ex.Message);
        }
    }

    /// <summary>Copies the system information (the text of system.txt).</summary>
    [RelayCommand]
    public async Task CopyDetailsAsync()
    {
        await CopyAsync(system.Build()).ConfigureAwait(true);
        logger.LogInformation("The problem report details were copied to the clipboard");
        Status = text["report.copied"];
    }

    private async Task CopyAsync(string value)
    {
        if (host.Active?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(value).ConfigureAwait(true);
        }
        else
        {
            logger.LogWarning("No window to copy to the clipboard from");
        }
    }
}

using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary><see cref="IReportDialog"/> as a small modal window; a new view model every time (its status line starts empty).</summary>
public sealed class ReportDialog(WindowHost host, Func<ReportProblemViewModel> models, ILogger<ReportDialog> logger) : IReportDialog
{
    /// <inheritdoc />
    public async Task ShowAsync()
    {
        if (host.Active is not { } owner)
        {
            logger.LogWarning("Report a problem was asked for without a window");
            return;
        }

        logger.LogInformation("Report a problem opened");
        await new ReportProblemWindow { DataContext = models() }.ShowDialog(owner).ConfigureAwait(true);
    }
}

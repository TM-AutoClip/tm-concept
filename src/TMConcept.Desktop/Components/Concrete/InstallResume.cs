using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Components;

/// <summary>
/// At start, after the main window opened and before the version check (28 Nov 2026, the developer: "yarıda kapattı, son yarım kalanı siler,
/// onu tekrar indirerek devam eder"): when the previous run closed in the middle of an install or a "Reinstall from scratch" (the install
/// journal still names components, <see cref="IInstallJournal"/>), the files of each unfinished component are deleted for good, and the
/// unfinished ones are installed again through the one install flow (one confirmation lists the downloads); the components that were
/// finished are kept and not downloaded again. Declined = they leave the journal and are installed when a step needs them; failed or
/// cancelled = they stay and the next start goes on.
/// </summary>
public sealed class InstallResume(ComponentPlanner planner, ComponentInstallFlow flow, IComponentWorkGate gate, IActivityLog activity, Localizer text,
    ILogger<InstallResume> logger)
{
    /// <summary>The text of what the install confirmation says a cancel means here.</summary>
    public const string PurposeKey = "install.purpose.resume";

    /// <summary>Resumes what the previous run left unfinished; null when there was nothing.</summary>
    public async Task<InstallOutcome?> RunAsync(CancellationToken cancellationToken)
    {
        if (planner.Unfinished().Count == 0)
        {
            return null;
        }

        IReadOnlyList<string> left;
        using (gate.Install())
        {
            try
            {
                left = await planner.ClearUnfinishedAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A file in use: nothing is downloaded onto it; the journal stays, so the next start tries again.
                logger.LogError(LogEvents.OnScreen, ex, "The files of an unfinished install could not be deleted; it is tried again at the next start");
                var line = text.Format("install.resume.delete_failed", ex.Message);
                activity.Add(ActivityLevel.Error, line);
                return new InstallOutcome(false, line);
            }
        }

        if (left.Count == 0)
        {
            return null;
        }

        var names = string.Join(", ", left.Select(c => planner.Find(c)?.Name ?? c));
        logger.LogInformation("The previous run closed in the middle of installing {Codes}; installing them again", string.Join(", ", left));
        activity.Add(ActivityLevel.Info, text.Format("install.resume.found", names));

        // What this computer can no longer install (another graphics card, a dependency without a download here) leaves the journal: it would
        // otherwise be named at every start.
        var plan = await planner.PlanAsync(left, cancellationToken).ConfigureAwait(true);
        var unsupported = plan.Unsupported.Select(c => c.Code).Where(left.Contains).ToList();
        if (unsupported.Count > 0)
        {
            planner.Forget(unsupported);
            logger.LogInformation("Unfinished installs that are not available on this computer are forgotten: {Codes}", string.Join(", ", unsupported));
        }

        var outcome = await flow.EnsureAsync(left, PurposeKey, null, cancellationToken).ConfigureAwait(true);
        if (outcome.Declined)
        {
            planner.Forget(left);
            logger.LogInformation("The unfinished installs were declined; they are installed when a step needs them");
        }

        return outcome;
    }
}

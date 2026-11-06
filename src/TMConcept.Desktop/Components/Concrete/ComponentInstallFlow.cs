using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Core.Engines;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Components;

/// <summary>
/// The one install flow of the app (saving models, the first video): plans what the given components need, shows ONE confirmation that
/// lists every download (name, source host, size), installs with progress (the page and the activity log) and says why when something is
/// not ready afterwards. Nothing is downloaded without the confirmation.
/// </summary>
public sealed class ComponentInstallFlow(ComponentPlanner planner, IConfirmDialog confirm, IActivityLog activity, Localizer text,
    ILogger<ComponentInstallFlow> logger)
{
    /// <summary>The planner.</summary>
    public ComponentPlanner Planner => planner;

    /// <summary>
    /// Makes <paramref name="codes"/> ready. <paramref name="purposeKey"/> names what waits for it in the confirmation ("…the settings are not
    /// saved" / "…the video is not made").
    /// </summary>
    public async Task<InstallOutcome> EnsureAsync(IReadOnlyCollection<string> codes, string purposeKey, IProgress<ToolProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var plan = await planner.PlanAsync(codes, cancellationToken).ConfigureAwait(true);
        if (plan.Unsupported.Count > 0)
        {
            var names = string.Join(", ", plan.Unsupported.Select(c => c.Name));
            logger.LogWarning("Not available on this computer: {Components}", string.Join(", ", plan.Unsupported.Select(c => c.Code)));
            return new InstallOutcome(false, text.Format("install.unsupported", names));
        }

        if (plan.Missing.Count == 0)
        {
            return new InstallOutcome(true, string.Empty);
        }

        var answer = await confirm.AskAsync(text["install.title"], Message(plan, purposeKey), text["install.confirm"], danger: false).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            logger.LogInformation("The download of {Components} was declined", string.Join(", ", plan.Missing.Select(c => c.Code)));
            return new InstallOutcome(false, text["install.declined"]);
        }

        var names2 = string.Join(", ", plan.Missing.Select(c => c.Name));
        activity.Add(ActivityLevel.Info, text.Format("install.started", names2));
        try
        {
            await planner.InstallAsync(plan, progress, cancellationToken).ConfigureAwait(true);
        }
        catch (ToolInstallException ex)
        {
            var reason = text.Format("install.failed", text.Format("tools.fail." + ex.Code, ex.Code == "unavailable" ? "ffmpeg" : string.Empty));
            activity.Add(ActivityLevel.Error, reason);
            return new InstallOutcome(false, reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException
            or InvalidDataException or EngineException)
        {
            // The planner logged the exception with the component; the user gets its message.
            var reason = text.Format("install.failed", ex.Message);
            activity.Add(ActivityLevel.Error, reason);
            return new InstallOutcome(false, reason);
        }

        activity.Add(ActivityLevel.Info, text.Format("install.done", names2));
        return new InstallOutcome(true, string.Empty);
    }

    private string Message(InstallPlan plan, string purposeKey)
    {
        var message = new StringBuilder(text["install.message"]).Append("\n\n");
        foreach (var download in plan.Downloads)
        {
            message.Append("• ").Append(download.Name).Append(" — ").Append(download.Host).Append(" — ~").Append(Megabytes(download.ApproximateBytes)).Append(" MB\n");
        }

        message.Append('\n').Append(text.Format("install.total", Megabytes(plan.TotalBytes))).Append(' ').Append(text[purposeKey]);
        return message.ToString();
    }

    /// <summary>Megabytes, rounded (at least 1).</summary>
    public static string Megabytes(long bytes) => Math.Max(1, Math.Round(bytes / 1_000_000.0)).ToString("#,0", CultureInfo.InvariantCulture);
}

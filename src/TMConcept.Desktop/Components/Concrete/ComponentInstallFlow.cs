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
/// lists every download (name, source host, size), then installs inside the modal install window (<see cref="IInstallDialog"/>: one line
/// per component with its progress, Cancel; the app cannot be used meanwhile, a failure stays on screen until it is closed) and says why
/// when something is not ready afterwards. The waiting work (a video, a concept run) goes on after the window closed. Nothing is
/// downloaded without the confirmation. A cancellation during the install is an outcome, not an error. Every step goes to the log file.
/// </summary>
public sealed class ComponentInstallFlow(ComponentPlanner planner, IConfirmDialog confirm, IInstallDialog window, IActivityLog activity, Localizer text,
    Work.WorkStatus work, Core.Paths.IAppPaths paths, IComponentWorkGate gate, ILogger<ComponentInstallFlow> logger)
{
    /// <summary>The planner.</summary>
    public ComponentPlanner Planner => planner;

    /// <summary>
    /// Makes <paramref name="codes"/> ready. <paramref name="purposeKey"/> names what waits for it in the confirmation ("…the settings are not
    /// saved" / "…the video is not made"). <paramref name="progress"/> also gets the progress (the page's own bar).
    /// </summary>
    public async Task<InstallOutcome> EnsureAsync(IReadOnlyCollection<string> codes, string purposeKey, IProgress<ToolProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var plan = await planner.PlanAsync(codes, cancellationToken).ConfigureAwait(true);

        // What has no download for this computer is reported, but does not hold back the others (an unrelated step is still installed).
        var unsupported = string.Empty;
        if (plan.Unsupported.Count > 0)
        {
            logger.LogWarning("Not available on this computer: {Components}", string.Join(", ", plan.Unsupported.Select(c => c.Code)));
            unsupported = text.Format("install.unsupported", string.Join(", ", plan.Unsupported.Select(c => c.Name)));
        }

        if (plan.Missing.Count == 0)
        {
            return new InstallOutcome(unsupported.Length == 0, unsupported);
        }

        var answer = await confirm.AskAsync(text["install.title"], Message(plan, purposeKey), text["install.confirm"], danger: false).ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            logger.LogInformation("The download of {Components} was declined", string.Join(", ", plan.Missing.Select(c => c.Code)));
            return new InstallOutcome(false, text["install.declined"]);
        }

        var names = string.Join(", ", plan.Missing.Select(c => c.Name));
        activity.Add(ActivityLevel.Info, text.Format("install.started", names));
        using var model = new InstallViewModel(plan.Missing, text);

        // 0.1.37: the start-up sweep does not touch the components folder while this install writes into it.
        using var hold = gate.Install();
        var cancelled = false;
        string error;
        try
        {
            error = await window.RunAsync(model, async m =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m.Cancellation);
                var overall = new Progress<ToolProgress>(p =>
                {
                    m.Percent = Math.Round(p.Share * 100);
                    work.Show(this, text.Format("work.install", m.Percent, names));
                    progress?.Report(p);
                });
                try
                {
                    await planner.InstallAsync(plan, overall, linked.Token, new Progress<ComponentStep>(m.Apply)).ConfigureAwait(true);
                    return string.Empty;
                }
                catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
                {
                    // Cancel pressed (in the window or on the page): what was installed before stays; the rest is downloaded the next time.
                    logger.LogInformation("The install of {Components} was cancelled", string.Join(", ", plan.Missing.Select(c => c.Code)));
                    activity.Add(ActivityLevel.Warning, text.Format("install.cancelled.log", names));
                    cancelled = true;
                    return string.Empty;
                }
                catch (ToolInstallException ex)
                {
                    var reason = text.Format("install.failed", Reason(text, ex));
                    activity.Add(ActivityLevel.Error, reason);
                    return reason;
                }
                catch (OperationCanceledException ex)
                {
                    // Not the user's cancel: something timed out (a download's own time-out is a ToolInstallException "timeout").
                    logger.LogWarning(ex, "The install of {Components} timed out", string.Join(", ", plan.Missing.Select(c => c.Code)));
                    var reason = text.Format("install.failed", text.Format("tools.fail.timeout", ex.Message));
                    activity.Add(ActivityLevel.Error, reason);
                    return reason;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException
                    or InvalidDataException or EngineException)
                {
                    // The planner logged the exception with the component; the user gets its message.
                    var reason = text.Format("install.failed", ex.Message);
                    activity.Add(ActivityLevel.Error, reason);
                    return reason;
                }
            }).ConfigureAwait(true);
        }
        finally
        {
            // Also when the window ends with an exception nobody expected: the strip's step and spinner never stay behind.
            work.Clear(this);
        }

        if (cancelled)
        {
            return new InstallOutcome(false, text["install.cancelled"]);
        }

        if (error.Length > 0)
        {
            return new InstallOutcome(false, error);
        }

        activity.Add(ActivityLevel.Info, text.Format("install.done", names));
        return new InstallOutcome(unsupported.Length == 0, unsupported);
    }

    /// <summary>
    /// Why an install failed, in the user's language, with the system's own reason where there is one (an SSL/TLS or DNS error, a reset
    /// connection: 0.1.16 showed only "the download failed").
    /// </summary>
    public static string Reason(Localizer text, ToolInstallException error)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(error);
        var detail = error.Code == "unavailable" ? "ffmpeg" : error.Detail.Length > 0 ? error.Detail : error.Message;
        var key = "tools.fail." + error.Code;
        return text.Has(key) ? text.Format(key, detail) : text.Format("tools.fail.download", detail);
    }

    private string Message(InstallPlan plan, string purposeKey)
    {
        var message = new StringBuilder(text["install.message"]).Append("\n\n");
        foreach (var download in plan.Downloads)
        {
            message.Append("• ").Append(download.Name).Append(" — ").Append(download.Host).Append(" — ~").Append(Megabytes(download.ApproximateBytes)).Append(" MB\n");
        }

        message.Append('\n').Append(text.Format("install.total", Megabytes(plan.TotalBytes))).Append(' ').Append(text[purposeKey]);
        if (FreeSpace() is { } free && free < SpaceNeeded(plan.TotalBytes))
        {
            logger.LogWarning("The tools drive has {Free} MB free; the install needs about {Needed} MB", free / 1_000_000, SpaceNeeded(plan.TotalBytes) / 1_000_000);
            message.Append("\n\n").Append(text.Format("install.disk_short", Megabytes(free), Megabytes(SpaceNeeded(plan.TotalBytes))));
        }

        return message.ToString();
    }

    /// <summary>
    /// What an install takes on the disk: the downloads twice (an archive and what is unpacked from it; the Python packages unpack to about
    /// twice their wheels), plus 1 GB to spare.
    /// </summary>
    public static long SpaceNeeded(long downloadBytes) => (downloadBytes * 2) + 1_000_000_000;

    /// <summary>The free space of the drive that holds the tools folder; null when it cannot be read.</summary>
    private long? FreeSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(paths.ToolsDirectory));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Megabytes, rounded (at least 1).</summary>
    public static string Megabytes(long bytes) => Math.Max(1, Math.Round(bytes / 1_000_000.0)).ToString("#,0", CultureInfo.InvariantCulture);
}

using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Startup;

/// <inheritdoc cref="IStartUpdateOffer" />
public sealed class StartUpdateOffer(IAppUpdateDialog dialog, TimeProvider clock, ILogger<StartUpdateOffer> logger) : IStartUpdateOffer
{
    /// <inheritdoc />
    public async Task<StartUpdateOutcome> OfferFirstAsync(Task<UpdateOffer?> check, TimeSpan bound)
    {
        ArgumentNullException.ThrowIfNull(check);
        using var stop = new CancellationTokenSource();
        var wait = Task.Delay(bound, clock, stop.Token);
        var first = await Task.WhenAny(check, wait).ConfigureAwait(true);
        await stop.CancelAsync().ConfigureAwait(true);
        if (first != check)
        {
            logger.LogInformation("The look for a newer version did not answer within {Seconds} s; the start goes on and offers it when it answers",
                bound.TotalSeconds);
            return StartUpdateOutcome.Pending;
        }

        return await OfferAsync(check).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<StartUpdateOutcome> OfferAsync(Task<UpdateOffer?> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        try
        {
            if (await check.ConfigureAwait(true) is not { } offer)
            {
                return StartUpdateOutcome.None;
            }

            var installed = await dialog.ShowAsync(offer).ConfigureAwait(true);
            logger.LogInformation("Version {Version} was {Answer} at the start", offer.Version, installed ? "installed" : "put off");
            return installed ? StartUpdateOutcome.Installed : StartUpdateOutcome.Later;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException
            or OperationCanceledException)
        {
            // No internet, GitHub's limit, a timeout: the running version goes on; the next start looks again.
            logger.LogWarning(ex, "The look for a newer version failed");
            return StartUpdateOutcome.None;
        }
    }
}

using Microsoft.Extensions.Logging;
using TMConcept.Core.Providers;

namespace TMConcept.Core.Chat;

/// <summary>
/// The one retry rule for answers that must be JSON (0.1.17, the developer: weak local models may not return valid JSON): when an answer
/// cannot be read (even with <see cref="ModelJson"/>'s tolerance), the SAME request is sent again, at most <see cref="ExtraAttempts"/> more
/// times; then a <see cref="ProviderException"/> with code <c>parse</c> ("the answer could not be read") ends the work. Online and local
/// models alike. Each attempt is a full paid call: the caller counts its cost inside <c>attempt</c> (every attempt, also the unreadable ones),
/// and every unreadable attempt is logged with its number.
/// </summary>
public static class JsonAnswerRetry
{
    /// <summary>Attempts after the first one.</summary>
    public const int ExtraAttempts = 2;

    /// <summary>All attempts (the first and <see cref="ExtraAttempts"/>).</summary>
    public const int Attempts = 1 + ExtraAttempts;

    /// <summary>
    /// Runs <paramref name="attempt"/> (its argument: the attempt's number, 1 to <see cref="Attempts"/>) until it returns a read answer; null =
    /// unreadable (asked again). A provider error, a cut answer the caller rethrows and cancellation end it at once. <paramref name="what"/>
    /// names the work in the log ("Topic suggestions").
    /// </summary>
    public static async Task<T> RunAsync<T>(string what, Func<int, CancellationToken, Task<T?>> attempt, ILogger logger, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(logger);
        for (var number = 1; ; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await attempt(number, cancellationToken).ConfigureAwait(false) is { } read)
            {
                if (number > 1)
                {
                    logger.LogInformation("{What}: the answer of attempt {Attempt} could be read", what, number);
                }

                return read;
            }

            if (number >= Attempts)
            {
                logger.LogError("{What}: no answer could be read as JSON in {Attempts} attempts; stopped", what, Attempts);
                throw new ProviderException($"The model's answer could not be read in {Attempts} attempts (no valid JSON).") { Code = "parse" };
            }

            logger.LogWarning("{What}: the answer of attempt {Attempt} could not be read as JSON; the same request is sent again ({Next} of {Attempts})", what,
                number, number + 1, Attempts);
        }
    }
}

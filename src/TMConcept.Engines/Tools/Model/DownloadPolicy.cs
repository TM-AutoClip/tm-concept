namespace TMConcept.Engines.Tools;

/// <summary>
/// How a download copes with a bad network: <paramref name="Attempts"/> tries per file with a growing pause between them (from
/// <paramref name="FirstDelay"/>, doubling, at most <paramref name="MaxDelay"/>), each continuing where the last one stopped; a try ends when
/// no data arrives for <paramref name="ReadTimeout"/> (not after a fixed time for the whole file: a 12 GB model on a slow line may take hours).
/// </summary>
public sealed record DownloadPolicy(int Attempts, TimeSpan FirstDelay, TimeSpan MaxDelay, TimeSpan ReadTimeout)
{
    /// <summary>5 tries, 2 s → 4 s → 8 s → 16 s pauses (at most 30 s), 60 s without data ends a try.</summary>
    public static DownloadPolicy Default { get; } = new(5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));

    /// <summary>The pause after the failed try number <paramref name="attempt"/> (1 = the first).</summary>
    public TimeSpan DelayAfter(int attempt)
    {
        var factor = Math.Pow(2, Math.Clamp(attempt - 1, 0, 20));
        var delay = TimeSpan.FromMilliseconds(FirstDelay.TotalMilliseconds * factor);
        return delay < MaxDelay ? delay : MaxDelay;
    }
}

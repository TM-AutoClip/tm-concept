namespace TMConcept.Providers.Common;

/// <summary>The HTTP layer of every provider call (tests replace it with a fake provider; nothing reaches the network then).</summary>
public sealed record ProviderTransport(Func<HttpMessageHandler> CreateHandler)
{
    /// <summary>The real network.</summary>
    public static ProviderTransport Network { get; } = new(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

    /// <summary>Wait after a 429 (tests make it instant).</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>
    /// Longest wait for one request that is read whole (long answers of thinking models take minutes). A streamed answer has only to start
    /// in this time; after that <see cref="StreamIdle"/> and <see cref="StreamLimit"/> apply.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// A streamed answer that sends nothing for this long has stopped (Anthropic sends a <c>ping</c> event every few seconds also while the
    /// model thinks, so minutes of silence mean a dead connection, not a slow model).
    /// </summary>
    public TimeSpan StreamIdle { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Longest time for a whole streamed answer. A video code answer of 32 000 tokens took 314–325 s (Opus 5.5, effort high, 7 Nov 2026), so
    /// a 64 000-token one takes about 11 minutes: the old single 15-minute limit for the whole request was too close.
    /// </summary>
    public TimeSpan StreamLimit { get; init; } = TimeSpan.FromMinutes(60);
}

namespace TMConcept.Providers.Common;

/// <summary>The HTTP layer of every provider call (tests replace it with a fake provider; nothing reaches the network then).</summary>
public sealed record ProviderTransport(Func<HttpMessageHandler> CreateHandler)
{
    /// <summary>The real network.</summary>
    public static ProviderTransport Network { get; } = new(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

    /// <summary>Wait after a 429 (tests make it instant).</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>Longest wait for one request (long answers of thinking models take minutes).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);
}

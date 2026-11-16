namespace TMConcept.Desktop.Updates;

/// <summary>How the update check reaches GitHub (tests give a fake server; no test reads the real GitHub).</summary>
public sealed record UpdateTransport(Func<HttpMessageHandler> CreateHandler)
{
    /// <summary>The real network.</summary>
    public static UpdateTransport Network { get; } = new(() => new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) });
}

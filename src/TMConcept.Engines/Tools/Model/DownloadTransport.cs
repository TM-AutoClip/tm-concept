namespace TMConcept.Engines.Tools;

/// <summary>How downloads reach the network (tests give a fake handler; nothing real is downloaded in tests).</summary>
public sealed record DownloadTransport(Func<HttpMessageHandler> CreateHandler)
{
    /// <summary>
    /// The real network: redirects are followed by the downloader itself (each hop's host is checked); a connection that cannot be made in
    /// 30 s fails that try; pooled connections are renewed every 5 minutes and kept alive with pings, so a long download over a quiet
    /// connection is not dropped by a router in between.
    /// </summary>
    public static DownloadTransport Network { get; } = new(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(30),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
    });
}

namespace TMConcept.Engines.Tools;

/// <summary>How downloads reach the network (tests give a fake handler; nothing real is downloaded in tests).</summary>
public sealed record DownloadTransport(Func<HttpMessageHandler> CreateHandler)
{
    /// <summary>The real network.</summary>
    public static DownloadTransport Network { get; } = new(() => new SocketsHttpHandler { AllowAutoRedirect = false });
}

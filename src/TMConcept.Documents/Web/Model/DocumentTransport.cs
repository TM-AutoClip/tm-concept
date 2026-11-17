namespace TMConcept.Documents.Web;

/// <summary>How web pages and articles are fetched (tests give a fake handler; nothing real is fetched in tests).</summary>
public sealed record DocumentTransport(Func<HttpMessageHandler> CreateHandler)
{
    /// <summary>The real network: up to 5 redirects, a connection must be made in 15 s.</summary>
    public static DocumentTransport Network { get; } = new(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    });
}

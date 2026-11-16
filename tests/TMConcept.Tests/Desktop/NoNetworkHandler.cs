namespace TMConcept.Tests.Desktop;

/// <summary>A network that is never reached: every request fails at once (the desktop tests never read GitHub or download anything real).</summary>
internal sealed class NoNetworkHandler : HttpMessageHandler
{
    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        throw new HttpRequestException("There is no network in the tests (" + request.RequestUri?.Host + ").");
    }
}

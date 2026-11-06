using System.Net;
using System.Text;
using TMConcept.Providers.Common;

namespace TMConcept.Tests.Providers;

/// <summary>
/// The default network of the desktop harness: nothing leaves the machine. A key test (a GET) is answered as passed, so a key a test saved
/// directly counts as working; any other request fails as if there were no network. Tests that check requests use <see cref="FakeHttp"/>.
/// </summary>
internal sealed class OfflineHttp : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    public ProviderTransport Transport => new(() => this) { Delay = (_, _) => Task.CompletedTask };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request.RequestUri!);
        }

        if (request.Method == HttpMethod.Get)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json") });
        }

        throw new HttpRequestException("No network in tests.");
    }
}

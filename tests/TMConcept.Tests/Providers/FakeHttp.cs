using System.Net;
using System.Text;
using TMConcept.Providers.Common;

namespace TMConcept.Tests.Providers;

/// <summary>A fake network: records every request and answers from a queue. No real provider is ever called.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _answers = new();

    public List<RecordedRequest> Requests { get; } = [];

    public List<TimeSpan> Delays { get; } = [];

    public ProviderTransport Transport => new(() => this)
    {
        Delay = (wait, _) =>
        {
            Delays.Add(wait);
            return Task.CompletedTask;
        },
    };

    public FakeHttp Answer(HttpStatusCode status, string body, string mediaType = "application/json", Action<HttpResponseMessage>? edit = null)
    {
        _answers.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
            edit?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body));
        if (_answers.Count == 0)
        {
            throw new InvalidOperationException("The fake network has no answer left.");
        }

        return _answers.Dequeue()();
    }
}

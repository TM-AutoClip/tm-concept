using System.Net;
using System.Text;

namespace TMConcept.Tests.Documents;

/// <summary>
/// Answers HTTP requests from saved answers in order (or by a function) and records each request: address, method, headers and body.
/// Nothing reaches the network.
/// </summary>
internal sealed class FixtureHttp : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();

    public List<(Uri Uri, string Method, Dictionary<string, string> Headers, string Body)> Requests { get; } = [];

    public FixtureHttp Json(string body, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers) =>
        Answer(status, "application/json", body, headers);

    public FixtureHttp Answer(HttpStatusCode status, string type, string body, params (string Name, string Value)[] headers)
    {
        _answers.Enqueue(request =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(type);
            var response = new HttpResponseMessage(status) { Content = content, RequestMessage = request };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!, request.Method.Method, headers, body));
        if (!_answers.TryDequeue(out var answer))
        {
            throw new InvalidOperationException("No saved answer left for " + request.RequestUri);
        }

        return answer(request);
    }
}

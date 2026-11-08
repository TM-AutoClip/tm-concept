using TMConcept.Core.Chat;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Providers;

/// <summary>A fake model: answers from a script and records every request. No network.</summary>
internal sealed class FakeChat : IChatClientFactory
{
    private readonly Queue<Func<ChatRequest, Task<ChatResponse>>> _answers = new();

    public List<ChatRequest> Requests { get; } = [];

    public List<string> KeysUsed { get; } = [];

    public FakeChat Answer(Func<ChatRequest, ChatResponse> answer)
    {
        _answers.Enqueue(r => Task.FromResult(answer(r)));
        return this;
    }

    /// <summary>An answer that takes its time (it can report progress and let the screen show it before it ends).</summary>
    public FakeChat AnswerAsync(Func<ChatRequest, Task<ChatResponse>> answer)
    {
        _answers.Enqueue(answer);
        return this;
    }

    public FakeChat Concepts(IEnumerable<string> titles, ChatUsage? usage = null) =>
        Answer(_ => new ChatResponse(Json(titles), usage ?? new ChatUsage(1000, 0, 0, 2000), null, "end_turn", "req-1"));

    public static string Json(IEnumerable<string> titles) =>
        "{\"konseptler\":[" + string.Join(",", titles.Select(t =>
            "{\"baslik\":\"" + t + "\",\"ozet\":\"" + Guid.NewGuid().ToString("N") + "\",\"icerik\":\"[on screen: map]\\nThe story of " + t + ".\"}")) + "]}";

    /// <summary>Titles that share no words or letter groups (so the local duplicate check keeps all of them).</summary>
    public static IReadOnlyList<string> Distinct(int count) =>
        Enumerable.Range(0, count).Select(_ => string.Join(" ", Guid.NewGuid().ToString("N").Chunk(8).Select(c => new string(c)))).ToList();

    public IChatClient Create(CatalogModel model, string apiKey)
    {
        KeysUsed.Add(apiKey);
        return new Client(this, model);
    }

    private sealed class Client(FakeChat owner, CatalogModel model) : IChatClient
    {
        public CatalogModel Model { get; } = model;

        public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.Requests.Add(request);
            if (owner._answers.Count == 0)
            {
                throw new InvalidOperationException("The fake model has no answer left.");
            }

            return owner._answers.Dequeue()(request);
        }
    }
}

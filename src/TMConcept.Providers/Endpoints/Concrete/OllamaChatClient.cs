using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Chat;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// A chat with an Ollama model through Ollama's own <c>POST /api/chat</c> (its OpenAI-compatible API cannot set the context, and Ollama's
/// default context on a card under 24 GB is 4 096 tokens, which cut long prompts off without a word): <c>options.num_ctx</c> is sent with
/// every request (the model's context as the user confirmed it), <c>options.num_predict</c> carries the answer limit (fitted into that
/// context), a schema goes as <c>format</c> (Ollama turns it into a grammar, so even a weak model writes valid JSON; <c>"json"</c> for a model
/// without the schema tick), <c>think</c> only for a model that can think, <c>keep_alive</c> so the model stays for the next batch (the app
/// unloads it when another local model needs the card) and images as base64 in the user message. A streamed call reads Ollama's lines
/// (one JSON object each); thinking arrives apart (<c>message.thinking</c>) and is never part of the answer. <c>done_reason: length</c> is a
/// cut answer with its text. A 400 with a format or think field is sent once more without them. After an answer one log line says how much
/// of the model sits on the graphics card and how much in main memory. A prompt that does not fit the context is refused before it is sent
/// ("too_large"), with <paramref name="tooLargeHint"/> saying what the user can do (null: give the model a larger context under API keys,
/// where the user's own servers are set up).
/// </summary>
public sealed class OllamaChatClient(LocalEndpoint endpoint, EndpointModel? saved, CatalogModel model, string token, OllamaServer server, LocalServerHttp http,
    ILogger logger, string? tooLargeHint = null) : IChatClient
{
    /// <summary>What a prompt too large for the context of a user's own server's model can do.</summary>
    public const string LargerContextHint = "Give the model a larger context in Settings → API keys.";

    private static readonly string[] Optional = ["format", "think"];
    private readonly Uri _root = LocalServerAddress.Root(endpoint);

    /// <inheritdoc />
    public CatalogModel Model { get; } = model;

    private string ServerName => endpoint.Name.Length > 0 ? endpoint.Name : OllamaServer.DefaultName;

    /// <summary>The context sent as <c>num_ctx</c>.</summary>
    public long Context => Model.ContextTokens > 0 ? Model.ContextTokens : EndpointDefaults.ContextTokens;

    /// <summary>The request body (pure; tests read it).</summary>
    public JsonObject Body(ChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var messages = new JsonArray();
        if (request.System.Length > 0)
        {
            messages.Add((JsonNode)new JsonObject { ["role"] = "system", ["content"] = request.System });
        }

        var user = new JsonObject { ["role"] = "user", ["content"] = request.User };
        if (request.Images is { Count: > 0 } images)
        {
            user["images"] = new JsonArray(images.Select(i => (JsonNode)JsonValue.Create(Convert.ToBase64String(i.Data))).ToArray());
        }

        messages.Add((JsonNode)user);
        var options = new JsonObject { ["num_ctx"] = Context };
        var limit = Limit(request);
        if (limit > 0)
        {
            options["num_predict"] = limit;
        }

        var body = new JsonObject
        {
            ["model"] = Model.Id,
            ["messages"] = messages,
            ["stream"] = request.Stream,
            ["keep_alive"] = http.Transport.OllamaKeepAlive,
            ["options"] = options,
        };
        if (request.Schema is not null)
        {
            body["format"] = Model.Capabilities.HasFlag(ModelCapability.Schema) ? request.Schema.DeepClone() : JsonValue.Create("json");
        }

        if (saved is { CanThink: true })
        {
            body["think"] = saved.Think;
        }

        return body;
    }

    /// <inheritdoc />
    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Room(request) < OpenAICompatibleChatClient.SmallestAnswer)
        {
            logger.LogWarning("{Provider} {Model}: the prompt (about {Prompt} tokens) does not fit the context of {Context} tokens; not sent", ServerName,
                Model.Id, OpenAICompatibleChatClient.PromptTokens(request), Context);
            throw new ProviderException(string.Create(CultureInfo.InvariantCulture,
                $"{ServerName}: the prompt (about {OpenAICompatibleChatClient.PromptTokens(request)} tokens) does not fit the context of {Model.Id} ({Context} tokens). {tooLargeHint ?? LargerContextHint}"))
            {
                Code = "too_large",
            };
        }

        var body = Body(request);
        var watch = Stopwatch.StartNew();
        var progress = new ChatProgressReporter(request.Progress, TimeProvider.System);
        Answer answer;
        try
        {
            answer = await AnswerAsync(body, request.Stream, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Status == 400 && Optional.Any(body.ContainsKey))
        {
            var rejected = Optional.Where(body.ContainsKey).ToList();
            logger.LogWarning("{Provider} {Model}: {Fields} was rejected; sending once more without it", ServerName, Model.Id, string.Join(", ", rejected));
            foreach (var field in rejected)
            {
                body.Remove(field);
            }

            answer = await AnswerAsync(body, request.Stream, progress, cancellationToken).ConfigureAwait(false);
        }

        var usage = new ChatUsage(answer.Prompt, 0, 0, answer.Output);
        var text = ThinkBlocks.Strip(answer.Text);
        var bad = answer.Error.Length > 0 || !answer.Done || answer.Reason == "length";
        logger.Log(bad ? LogLevel.Warning : LogLevel.Information,
            "{Provider} {Model}: done reason {Reason}, num_ctx {Context}, num_predict {Predict}, think {Think}, tokens in {Input} / out {Output}, thinking {Thinking} characters, {Ms} ms",
            ServerName, Model.Id, answer.Reason.Length > 0 ? answer.Reason : "-", Context, body["options"]?["num_predict"]?.ToJsonString() ?? "-",
            body["think"]?.ToJsonString() ?? "-", usage.Input, usage.Output, answer.Thinking, watch.ElapsedMilliseconds);
        if (answer.Error.Length > 0)
        {
            throw http.Http.Failure(ServerName, 502, new JsonObject { ["error"] = answer.Error }.ToJsonString(), string.Empty, token, usage);
        }

        progress.Done(text);
        await ReportAsync(cancellationToken).ConfigureAwait(false);
        if (!answer.Done)
        {
            throw new ProviderException($"{ServerName}: the answer stream ended early.")
            {
                Code = "incomplete",
                ChargedUsage = usage.Prompt > 0 || usage.Output > 0 ? usage : null,
                PartialText = text,
            };
        }

        return answer.Reason == "length"
            ? throw new ProviderException($"{ServerName}: the answer was cut off.")
            {
                Code = "truncated",
                ChargedUsage = usage,
                PartialText = text,
                StopReason = answer.Reason,
            }
            : new ChatResponse(text, usage, null, answer.Reason.Length > 0 ? answer.Reason : "stop", string.Empty);
    }

    /// <summary>The answer limit: the request's, lowered to the model's and to what the context leaves next to the prompt.</summary>
    private int Limit(ChatRequest request)
    {
        var limit = request.MaxOutputTokens > 0 ? request.MaxOutputTokens : int.MaxValue;
        if (Model.MaxOutputTokens > 0)
        {
            limit = Math.Min(limit, Model.MaxOutputTokens);
        }

        limit = Math.Min(limit, Room(request));
        return limit == int.MaxValue ? 0 : limit;
    }

    private int Room(ChatRequest request) => (int)Math.Clamp(Context - OpenAICompatibleChatClient.PromptTokens(request), 0, int.MaxValue);

    private async Task ReportAsync(CancellationToken cancellationToken)
    {
        try
        {
            await server.ReportAsync(_root, token, Model.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogInformation("{Provider}: the memory of {Model} could not be read ({Error})", ServerName, Model.Id, ex.GetType().Name);
        }
    }

    private async Task<Answer> AnswerAsync(JsonObject body, bool stream, ChatProgressReporter progress, CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        var address = new Uri(_root, "api/chat");
        HttpRequestMessage Create()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, address) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            LocalServerHttp.Authorize(message, token);
            return message;
        }

        progress.Sent();
        if (!stream)
        {
            var reply = await http.Http.SendAsync(Create, ServerName, token, cancellationToken).ConfigureAwait(false);
            var answer = new Answer();
            answer.Add(reply.Text);
            return answer;
        }

        var streamed = new Answer();
        await http.Http.StreamAsync(Create, ServerName, token, line =>
        {
            streamed.Add(line);
            progress.Line(() => ThinkBlocks.Strip(streamed.Text));
        }, cancellationToken).ConfigureAwait(false);
        if (streamed.Skipped > 0)
        {
            logger.LogWarning("{Provider} {Model}: {Count} stream line(s) could not be read and were skipped", ServerName, Model.Id, streamed.Skipped);
        }

        return streamed;
    }

    /// <summary>An answer of <c>/api/chat</c> read from one JSON object (whole) or from its stream's lines.</summary>
    private sealed class Answer
    {
        private readonly StringBuilder _text = new();

        public string Text => _text.ToString();

        public long Thinking { get; private set; }

        public bool Done { get; private set; }

        public string Reason { get; private set; } = string.Empty;

        public string Error { get; private set; } = string.Empty;

        public long Prompt { get; private set; }

        public long Output { get; private set; }

        public int Skipped { get; private set; }

        public void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                Skipped++;
                return;
            }

            if (node is not JsonObject obj)
            {
                Skipped++;
                return;
            }

            if (JsonRead.Text(obj["error"]) is { Length: > 0 } error)
            {
                Error = error;
                return;
            }

            _text.Append(JsonRead.Text(obj["message"]?["content"]));
            Thinking += JsonRead.Text(obj["message"]?["thinking"]).Length;
            if (obj["done"] is JsonValue done && done.TryGetValue<bool>(out var finished) && finished)
            {
                Done = true;
                Reason = JsonRead.Text(obj["done_reason"]);
                Prompt = JsonRead.Long(obj["prompt_eval_count"]);
                Output = JsonRead.Long(obj["eval_count"]);
            }
        }
    }
}

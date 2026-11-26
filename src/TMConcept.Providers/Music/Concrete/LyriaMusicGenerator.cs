using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Providers.Common;
using TMConcept.Providers.Speech;

namespace TMConcept.Providers.Music;

/// <summary>
/// Google Lyria music (0.1.46, C5) with the Gemini key: <c>POST v1beta/interactions</c> with the <c>Api-Revision: 2026-05-20</c> header and the
/// key in <c>x-goog-api-key</c>; the body is <c>{"model": …, "input": "…"}</c> (the music generation page of aistudio.google.com, read on 24 Nov
/// 2026). The answer's <c>steps</c> hold the audio as base64 in a <c>model_output</c> step's <c>content</c> block of <c>type: "audio"</c>;
/// it is MP3 by default. <c>lyria-3-clip-preview</c> always makes 30 s ($0.04 a piece), <c>lyria-3.5</c> a whole song whose length is only
/// asked for in the prompt ($0.08 a song). No field for "instrumental" or a length is documented: both go into the prompt
/// (<see cref="MusicPromptBuilder"/>). Not verified against the live service (no request was sent while this was written): the header value
/// and the answer shape are the page's.
/// </summary>
public sealed class LyriaMusicGenerator(GoogleProvider provider, ProviderHttp http) : IMusicGenerator
{
    /// <summary>The API revision the page's examples send.</summary>
    public const string ApiRevision = "2026-05-20";

    /// <inheritdoc />
    public bool Handles(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == provider.Code && model.Capabilities.HasFlag(ModelCapability.Music);
    }

    /// <inheritdoc />
    public bool Ready(CatalogModel model) => Handles(model);

    /// <summary>The request body: the model and the prompt as plain text.</summary>
    public static string Body(string model, string prompt) => new JsonObject { ["model"] = model, ["input"] = prompt }.ToJsonString();

    /// <summary>The audio of an answer (the last audio block of the model's output steps); null when it has none.</summary>
    public static byte[]? Audio(byte[] answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(answer);
        }
        catch (JsonException)
        {
            return null;
        }

        string? data = null;
        foreach (var step in root?["steps"] as JsonArray ?? [])
        {
            if (step?["type"]?.GetValue<string>() != "model_output")
            {
                continue;
            }

            foreach (var block in step["content"] as JsonArray ?? [])
            {
                if (block?["type"]?.GetValue<string>() == "audio" && block["data"]?.GetValue<string>() is { Length: > 0 } text)
                {
                    data = text;
                }
            }
        }

        if (data is null)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var model = request.Model.Model;
        var body = Body(model.Id, request.Prompt);
        var reply = await http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "interactions"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            message.Headers.TryAddWithoutValidation("Api-Revision", ApiRevision);
            provider.Authorize(message, request.Model.Key.Secret);
            return message;
        }, provider.Name, request.Model.Key.Secret, cancellationToken).ConfigureAwait(false);

        var audio = Audio(reply.Body);
        if (audio is not { Length: > 0 })
        {
            throw new MusicGenerationException("Lyria returned no audio.") { Code = MusicFailure.Empty };
        }

        Directory.CreateDirectory(request.OutputFolder);
        var file = Path.Combine(request.OutputFolder, "lyria.mp3");
        await File.WriteAllBytesAsync(file, audio, cancellationToken).ConfigureAwait(false);
        return new GeneratedMusic(file, MusicPricing.Usd(model, request.Seconds), MusicPricing.Exact(model));
    }
}

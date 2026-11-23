using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Speech;

/// <summary>
/// Gemini speech (0.1.35; <c>POST v1beta/models/{model}:generateContent</c> with <c>responseModalities: AUDIO</c>, the key in
/// <c>x-goog-api-key</c>; ai.google.dev / aistudio.google.com speech generation docs read on 23 Nov 2026). The 3.8 models answer a whole WAV
/// file (24 kHz, mono, 16-bit) as base64 at <c>candidates[0].content.parts[0].inlineData.data</c>; older ones raw PCM, which gets a header.
/// Thirty prebuilt voices, every content language (Turkish included). A delivery marker becomes the turn's style
/// (<c>speech_metadata.style</c>, "excited") or an inline tag (<c>&lt;laugh&gt;</c>, <c>&lt;sigh&gt;</c>, <c>&lt;whispers&gt;</c>). No speed
/// of its own (the app stretches the clip). The cost is the catalog's estimate per character.
/// </summary>
public sealed class GeminiSpeechEngine(GoogleProvider provider, ProviderHttp http) : ISpeechEngine
{
    /// <summary>The prebuilt voices (the speech generation docs).</summary>
    public static readonly IReadOnlyList<string> VoiceIds =
    [
        "Kore", "Puck", "Charon", "Zephyr", "Fenrir", "Leda", "Orus", "Aoede", "Callirrhoe", "Autonoe", "Enceladus", "Iapetus", "Umbriel", "Algieba",
        "Despina", "Erinome", "Algenib", "Rasalgethi", "Laomedeia", "Achernar", "Alnilam", "Schedar", "Gacrux", "Pulcherrima", "Achird",
        "Zubenelgenubi", "Vindemiatrix", "Sadachbia", "Sadaltager", "Sulafat",
    ];

    /// <summary>The sample rate of the audio when it comes as raw PCM.</summary>
    public const int PcmRate = 24000;

    /// <inheritdoc />
    public string Code => provider.Code;

    /// <inheritdoc />
    public string Name => "Gemini";

    /// <inheritdoc />
    public bool IsLocal => false;

    /// <inheritdoc />
    public bool Ready => true;

    /// <inheritdoc />
    public bool HandlesSpeed => false;

    /// <inheritdoc />
    public bool Supports(string language) => true;

    /// <inheritdoc />
    public IReadOnlyList<SpeechVoice> Voices(string language) => VoiceIds.Select(v => new SpeechVoice(v, v)).ToList();

    /// <inheritdoc />
    public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The request body: the text (with an inline tag for a sound), the style of a mood, the voice.</summary>
    public static string Body(string text, string voice, string emotion)
    {
        var (style, tag) = SpeechDeliveries.Gemini(emotion);
        var part = new JsonObject { ["text"] = tag.Length > 0 ? tag + " " + text : text };
        if (style.Length > 0)
        {
            part["speech_metadata"] = new JsonObject { ["style"] = style };
        }

        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(part) }),
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray("AUDIO"),
                ["speechConfig"] = new JsonObject { ["voiceConfig"] = new JsonObject { ["voice"] = VoiceIds.Contains(voice) ? voice : VoiceIds[0] } },
            },
        }.ToJsonString();
    }

    /// <inheritdoc />
    public async Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var online = request.Online ?? throw new SpeechException("No online speech model was given.") { Code = "model" };
        var body = Body(request.Text, request.Voice, request.Emotion);
        var reply = await http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "models/" + Uri.EscapeDataString(online.Model.Id) + ":generateContent"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            provider.Authorize(message, online.Key.Secret);
            return message;
        }, provider.Name, online.Key.Secret, cancellationToken).ConfigureAwait(false);

        var data = JsonNode.Parse(reply.Body)?["candidates"]?[0]?["content"]?["parts"]?[0]?["inlineData"]?["data"]?.GetValue<string>();
        if (string.IsNullOrEmpty(data))
        {
            throw new SpeechException("Gemini returned no audio.") { Code = "engine" };
        }

        await SpeechAudio.WriteAsync(request.OutputWav, Convert.FromBase64String(data), PcmRate, cancellationToken).ConfigureAwait(false);
        var cost = request.Text.Length * online.Model.SpeechUsdPerMillionCharacters / 1_000_000m;
        return new SpeechClip(request.OutputWav, WaveFile.DurationMs(request.OutputWav), request.Text.Length,
            decimal.Round(cost, 6, MidpointRounding.AwayFromZero), Code + "/" + online.Model.Id.ToString(CultureInfo.InvariantCulture));
    }
}

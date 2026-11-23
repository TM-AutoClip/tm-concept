using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Speech;

/// <summary>
/// Google Cloud Text-to-Speech, Chirp 3: HD voices (0.1.35; <c>POST v1/text:synthesize</c>; the docs read on 23 Nov 2026). The voice name is
/// <c>&lt;locale&gt;-Chirp3-HD-&lt;star&gt;</c> (tr-TR-Chirp3-HD-Achernar), LINEAR16 comes back as a base64 WAV in <c>audioContent</c>,
/// <c>speakingRate</c> 0.25–2 is the speed. The key goes in <c>x-goog-api-key</c> (a Google Cloud API key with the Text-to-Speech API
/// allowed; the docs show OAuth only, so this is the developer's open question). No delivery markers. Cost: the catalog's price per character.
/// </summary>
public sealed class GoogleCloudSpeechEngine(GoogleCloudProvider provider, ProviderHttp http) : ISpeechEngine
{
    /// <summary>The Chirp 3: HD voice stars (the same thirty names as Gemini's voices).</summary>
    public static IReadOnlyList<string> Stars => GeminiSpeechEngine.VoiceIds;

    /// <summary>The locale of each content language.</summary>
    public static readonly IReadOnlyDictionary<string, string> Locales = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ContentLanguages.Turkish] = "tr-TR", [ContentLanguages.English] = "en-US", [ContentLanguages.German] = "de-DE",
        [ContentLanguages.Spanish] = "es-ES", [ContentLanguages.French] = "fr-FR", [ContentLanguages.Italian] = "it-IT",
        [ContentLanguages.Portuguese] = "pt-BR", [ContentLanguages.Dutch] = "nl-NL", [ContentLanguages.Polish] = "pl-PL",
        [ContentLanguages.Russian] = "ru-RU", [ContentLanguages.Swedish] = "sv-SE", [ContentLanguages.Indonesian] = "id-ID",
    };

    /// <inheritdoc />
    public string Code => provider.Code;

    /// <inheritdoc />
    public string Name => "Google Cloud (Chirp 3 HD)";

    /// <inheritdoc />
    public bool IsLocal => false;

    /// <inheritdoc />
    public bool Ready => true;

    /// <inheritdoc />
    public bool Supports(string language) => Locales.ContainsKey(language ?? string.Empty);

    /// <inheritdoc />
    public IReadOnlyList<SpeechVoice> Voices(string language) => Stars.Select(s => new SpeechVoice(s, s)).ToList();

    /// <inheritdoc />
    public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The request body for <paramref name="language"/>'s locale and the chosen star.</summary>
    public static string Body(string text, string language, string voice, double speed)
    {
        var locale = Locales.TryGetValue(language, out var known) ? known : "en-US";
        var star = Stars.Contains(voice) ? voice : Stars[0];
        return new JsonObject
        {
            ["input"] = new JsonObject { ["text"] = text },
            ["voice"] = new JsonObject { ["languageCode"] = locale, ["name"] = locale + "-Chirp3-HD-" + star },
            ["audioConfig"] = new JsonObject { ["audioEncoding"] = "LINEAR16", ["speakingRate"] = Math.Clamp(speed, 0.25, 2.0) },
        }.ToJsonString();
    }

    /// <inheritdoc />
    public async Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var online = request.Online ?? throw new SpeechException("No online speech model was given.") { Code = "model" };
        var body = Body(request.Text, request.Language, request.Voice, request.Speed);
        var reply = await http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "./text:synthesize"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            provider.Authorize(message, online.Key.Secret);
            return message;
        }, provider.Name, online.Key.Secret, cancellationToken).ConfigureAwait(false);

        var data = JsonNode.Parse(reply.Body)?["audioContent"]?.GetValue<string>();
        if (string.IsNullOrEmpty(data))
        {
            throw new SpeechException("Google Cloud returned no audio.") { Code = "engine" };
        }

        await SpeechAudio.WriteAsync(request.OutputWav, Convert.FromBase64String(data), 24000, cancellationToken).ConfigureAwait(false);
        var cost = request.Text.Length * online.Model.SpeechUsdPerMillionCharacters / 1_000_000m;
        return new SpeechClip(request.OutputWav, WaveFile.DurationMs(request.OutputWav), request.Text.Length,
            decimal.Round(cost, 6, MidpointRounding.AwayFromZero), Code + "/" + online.Model.Id.ToString(CultureInfo.InvariantCulture));
    }
}

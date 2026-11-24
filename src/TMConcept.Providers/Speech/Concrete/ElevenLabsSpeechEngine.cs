using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Speech;

/// <summary>
/// ElevenLabs speech (0.1.35; <c>POST v1/text-to-speech/{voice_id}?output_format=pcm_24000</c>, the key in <c>xi-api-key</c>; the API
/// reference read on 23 Nov 2026). Raw 24 kHz PCM comes back and gets a WAV header. The models of the catalog (v4, v4 Turbo, Flash v2.5,
/// Multilingual v2) all speak Turkish; v3/v4 take an audio tag for a delivery marker ("[excited]"). The voices are ElevenLabs' long-standing
/// premade ones (<see cref="Premade"/>, always offered) and, from 0.1.47 (H4), the voices of the user's account (cloned and library
/// voices too) as last read by <see cref="AccountVoiceLibrary"/>, those that speak the output language (rule 11,
/// <see cref="AccountVoiceFilter"/>). No speed of its own here (its range is 0.7–1.2; the app stretches the clip instead). The cost is the
/// catalog's price per character.
/// </summary>
public sealed class ElevenLabsSpeechEngine(ElevenLabsProvider provider, ProviderHttp http, AccountVoiceLibrary? account = null) : ISpeechEngine
{
    /// <summary>The premade voices offered (id, name).</summary>
    public static readonly IReadOnlyList<SpeechVoice> Premade =
    [
        new("21m00Tcm4TlvDq8ikWAM", "Rachel"), new("pNInz6obpgDQGcFmaJgB", "Adam"), new("EXAVITQu4vr4xnSDxMaL", "Sarah"),
        new("ErXwobaYiN019PkySvjV", "Antoni"), new("TxGEqnHWrfWFTfGW9XjX", "Josh"), new("MF3mGyEYCl7XYWbV9V6O", "Elli"),
        new("VR6AewLTigWG4xSOukaG", "Arnold"), new("AZnzlk1XvdvUeBnXmlld", "Domi"),
    ];

    /// <summary>The rate of the PCM asked for.</summary>
    public const int PcmRate = 24000;

    /// <inheritdoc />
    public string Code => provider.Code;

    /// <inheritdoc />
    public string Name => "ElevenLabs";

    /// <inheritdoc />
    public bool IsLocal => false;

    /// <inheritdoc />
    public bool Ready => true;

    /// <inheritdoc />
    public bool HandlesSpeed => false;

    /// <inheritdoc />
    public bool Supports(string language) => true;

    /// <inheritdoc />
    public IReadOnlyList<SpeechVoice> Voices(string language) => Merge(Premade, account?.Voices(Code) ?? [], language);

    /// <summary>
    /// The fixed voices first, then the account's voices that speak <paramref name="language"/> by name (a premade voice of the account is
    /// already in the fixed list). A cloned or generated voice is marked so it can be told from a premade one of the same name.
    /// </summary>
    public static IReadOnlyList<SpeechVoice> Merge(IReadOnlyList<SpeechVoice> fixedVoices, IReadOnlyList<AccountVoice> voices, string language)
    {
        ArgumentNullException.ThrowIfNull(fixedVoices);
        ArgumentNullException.ThrowIfNull(voices);
        var ids = fixedVoices.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var extra = voices.Where(v => ids.Add(v.Id) && AccountVoiceFilter.Speaks(v, language))
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(v => new SpeechVoice(v.Id, v.Category is "premade" or "" ? v.Name : v.Name + " (" + v.Category + ")"));
        return [.. fixedVoices, .. extra];
    }

    /// <inheritdoc />
    public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The request body: the text with the delivery's audio tag (v3/v4), the model and the language (not for Multilingual v2).</summary>
    public static string Body(string text, string model, string language, string emotion)
    {
        var tag = SpeechDeliveries.ElevenLabs(emotion, model);
        var body = new JsonObject { ["text"] = tag.Length > 0 ? tag + " " + text : text, ["model_id"] = model };
        if (model != "eleven_multilingual_v2" && language.Length == 2)
        {
            body["language_code"] = language;
        }

        return body.ToJsonString();
    }

    /// <inheritdoc />
    public async Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var online = request.Online ?? throw new SpeechException("No online speech model was given.") { Code = "model" };
        var voice = request.Voice.Length > 0 && request.Voice.All(char.IsAsciiLetterOrDigit) ? request.Voice : Premade[0].Id;
        var body = Body(request.Text, online.Model.Id, request.Language, request.Emotion);
        var reply = await http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "text-to-speech/" + voice + "?output_format=pcm_24000"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            provider.Authorize(message, online.Key.Secret);
            return message;
        }, provider.Name, online.Key.Secret, cancellationToken).ConfigureAwait(false);

        await SpeechAudio.WriteAsync(request.OutputWav, reply.Body, PcmRate, cancellationToken).ConfigureAwait(false);
        var cost = request.Text.Length * online.Model.SpeechUsdPerMillionCharacters / 1_000_000m;
        return new SpeechClip(request.OutputWav, WaveFile.DurationMs(request.OutputWav), request.Text.Length,
            decimal.Round(cost, 6, MidpointRounding.AwayFromZero), Code + "/" + online.Model.Id.ToString(CultureInfo.InvariantCulture));
    }
}

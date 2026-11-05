using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Providers.Speech;

/// <summary>
/// OpenAI speech (<c>POST /v1/audio/speech</c>, WAV out) with the model chosen for the speech step and the key of that model. Only the
/// provider's own voices; no reference voice. The cost is the catalog price per character (an estimate; the API returns no usage).
/// </summary>
public sealed class OpenAISpeechEngine(OpenAIProvider provider, ProviderHttp http) : ISpeechEngine
{
    private static readonly string[] VoiceIds = ["alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse", "marin", "cedar"];

    /// <inheritdoc />
    public string Code => provider.Code;

    /// <inheritdoc />
    public string Name => "OpenAI";

    /// <inheritdoc />
    public bool IsLocal => false;

    /// <inheritdoc />
    public bool Ready => true;

    /// <inheritdoc />
    public bool Supports(string language) => true;

    /// <inheritdoc />
    public IReadOnlyList<SpeechVoice> Voices(string language) => VoiceIds.Select(v => new SpeechVoice(v, v)).ToList();

    /// <inheritdoc />
    public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var online = request.Online ?? throw new SpeechException("No online speech model was given.") { Code = "model" };
        var body = new JsonObject
        {
            ["model"] = online.Model.Id,
            ["input"] = request.Text,
            ["voice"] = VoiceIds.Contains(request.Voice) ? request.Voice : VoiceIds[0],
            ["response_format"] = "wav",
            ["speed"] = Math.Clamp(request.Speed, 0.25, 4.0),
        }.ToJsonString();
        var reply = await http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "audio/speech"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/wav"));
            provider.Authorize(message, online.Key.Secret);
            return message;
        }, provider.Name, online.Key.Secret, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputWav)!);
        await File.WriteAllBytesAsync(request.OutputWav, reply.Body, cancellationToken).ConfigureAwait(false);
        long ms;
        try
        {
            ms = WaveFile.DurationMs(request.OutputWav);
        }
        catch (InvalidDataException ex)
        {
            throw new SpeechException("The provider did not return a WAV file.", ex) { Code = "engine" };
        }

        var cost = request.Text.Length * online.Model.SpeechUsdPerMillionCharacters / 1_000_000m;
        return new SpeechClip(request.OutputWav, ms, request.Text.Length, decimal.Round(cost, 6, MidpointRounding.AwayFromZero), Code + "/" + online.Model.Id
            .ToString(CultureInfo.InvariantCulture));
    }
}

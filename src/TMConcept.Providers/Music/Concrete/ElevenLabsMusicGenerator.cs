using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Providers.Common;
using TMConcept.Providers.Speech;

namespace TMConcept.Providers.Music;

/// <summary>
/// ElevenLabs Music (0.1.46, C5) with the ElevenLabs key: <c>POST v1/music?output_format=mp3_44100_128</c>, the key in <c>xi-api-key</c>, the
/// body <c>{"prompt", "music_length_ms" (3 000–600 000), "model_id", "force_instrumental"}</c> (the API reference "Compose music", read on
/// 24 Nov 2026). The answer is the audio file itself. $0.15 a minute (elevenlabs.io/pricing/api); how the provider rounds a part of a minute
/// is not stated, so the cost is marked as estimated. Not verified against the live service (no request was sent while this was written).
/// </summary>
public sealed class ElevenLabsMusicGenerator(ElevenLabsProvider provider, ProviderHttp http) : IMusicGenerator
{
    /// <summary>The audio format asked for.</summary>
    public const string OutputFormat = "mp3_44100_128";

    /// <summary>The shortest length the API takes (ms).</summary>
    public const int MinMs = 3_000;

    /// <summary>The longest length the API takes (ms).</summary>
    public const int MaxMs = 600_000;

    /// <inheritdoc />
    public bool Handles(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == provider.Code && model.Capabilities.HasFlag(ModelCapability.Music);
    }

    /// <inheritdoc />
    public bool Ready(CatalogModel model) => Handles(model);

    /// <summary>The request body.</summary>
    public static string Body(string model, string prompt, int seconds, bool instrumental) => new JsonObject
    {
        ["prompt"] = prompt,
        ["music_length_ms"] = Math.Clamp(seconds * 1000, MinMs, MaxMs),
        ["model_id"] = model,
        ["force_instrumental"] = instrumental,
    }.ToJsonString();

    /// <summary>Whether an answer looks like audio (an MP3 frame or an ID3 header), not a JSON error that came with 200.</summary>
    public static bool IsAudio(byte[] answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer.Length > 3 && ((answer[0] == (byte)'I' && answer[1] == (byte)'D' && answer[2] == (byte)'3') || (answer[0] == 0xFF && (answer[1] & 0xE0) == 0xE0));
    }

    /// <inheritdoc />
    public async Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var model = request.Model.Model;
        var body = Body(model.Id, request.Prompt, request.Seconds, request.Instrumental);
        var reply = await http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "music?output_format=" + OutputFormat))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            provider.Authorize(message, request.Model.Key.Secret);
            return message;
        }, provider.Name, request.Model.Key.Secret, cancellationToken).ConfigureAwait(false);

        if (!IsAudio(reply.Body))
        {
            throw new MusicGenerationException("ElevenLabs returned no audio.") { Code = MusicFailure.Empty };
        }

        Directory.CreateDirectory(request.OutputFolder);
        var file = Path.Combine(request.OutputFolder, "elevenlabs.mp3");
        await File.WriteAllBytesAsync(file, reply.Body, cancellationToken).ConfigureAwait(false);
        var seconds = Math.Clamp(request.Seconds * 1000, MinMs, MaxMs) / 1000;
        return new GeneratedMusic(file, MusicPricing.Usd(model, seconds), MusicPricing.Exact(model));
    }
}

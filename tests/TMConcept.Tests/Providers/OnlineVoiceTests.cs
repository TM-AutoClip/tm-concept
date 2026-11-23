using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Keys;
using TMConcept.Core.Markers;
using TMConcept.Core.Models;
using TMConcept.Core.Speech;
using TMConcept.Providers.Common;
using TMConcept.Providers.Speech;
using TMConcept.Speech.Python;

namespace TMConcept.Tests.Providers;

/// <summary>
/// 0.1.35: the new voices (Gemini 3.8 Flash TTS, Google Cloud Chirp 3 HD, ElevenLabs v4) against a fake network (no real request is ever
/// made), the delivery markers and the local yazgı-tts-1 entry.
/// </summary>
public sealed class OnlineVoiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tmc-voices-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static ResolvedModel Model(string provider, string id, decimal perMillion) =>
        new(new CatalogModel { Provider = provider, Id = id, Name = id, Capabilities = ModelCapability.Speech, SpeechUsdPerMillionCharacters = perMillion },
            new ApiKey { Provider = provider, Label = "k", Secret = "fake-test-key-voice-0123" }, string.Empty);

    private static byte[] Pcm(int samples) => new byte[samples * 2];

    private static byte[] Wave(int samples)
    {
        var path = Path.GetTempFileName();
        try
        {
            WaveFile.WritePcm16(path, new float[samples], 24000);
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GeminiSendsTheVoiceAndTheMoodAndReadsTheWaveFromInlineData()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK,
            new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject
            {
                ["inlineData"] = new JsonObject { ["mimeType"] = "audio/wav", ["data"] = Convert.ToBase64String(Wave(24000)) },
            }) } }) }.ToJsonString());
        using var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var provider = new GoogleProvider(http, NullLogger<GoogleProvider>.Instance);
        var engine = new GeminiSpeechEngine(provider, http);
        var out1 = Path.Combine(_folder, "g.wav");

        var clip = await engine.SynthesizeAsync(new SpeechRequest("Merhaba dünya.", "tr", "Charon", 1, out1, Model("google", "gemini-3.8-flash-tts", 17m),
            SpeechDeliveries.Excited), CancellationToken.None);

        clip.DurationMs.ShouldBe(1000);
        var request = fake.Requests.Single();
        request.Uri.ToString().ShouldBe("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash-tts:generateContent");
        request.Headers["x-goog-api-key"].ShouldBe("fake-test-key-voice-0123");
        var body = JsonNode.Parse(request.Body)!;
        body["generationConfig"]!["responseModalities"]![0]!.GetValue<string>().ShouldBe("AUDIO");
        body["generationConfig"]!["speechConfig"]!["voiceConfig"]!["voice"]!.GetValue<string>().ShouldBe("Charon");
        var part = body["contents"]![0]!["parts"]![0]!;
        part["text"]!.GetValue<string>().ShouldBe("Merhaba dünya.", "a mood is not read aloud");
        part["speech_metadata"]!["style"]!.GetValue<string>().ShouldContain("excited");
        engine.HandlesSpeed.ShouldBeFalse("Gemini has no rate: the app stretches the clip");
        JsonNode.Parse(GeminiSpeechEngine.Body("Ah.", "nobody", SpeechDeliveries.Laugh))!["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>()
            .ShouldBe("<laugh> Ah.", "a sound is an inline tag");
    }

    [Fact]
    public async Task ARawPcmAnswerGetsAWaveHeaderAndAnErrorPageIsRefused()
    {
        var fake = new FakeHttp().Answer(() => new ByteArrayContent(Pcm(12000))).Answer(() => new ByteArrayContent(Encoding.UTF8.GetBytes("{\"error\":1}")));
        using var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var engine = new ElevenLabsSpeechEngine(new ElevenLabsProvider(http, NullLogger<ElevenLabsProvider>.Instance), http);
        var clip = await engine.SynthesizeAsync(new SpeechRequest("Selam.", "tr", "pNInz6obpgDQGcFmaJgB", 1, Path.Combine(_folder, "e.wav"),
            Model("elevenlabs", "eleven_v4", 80m), SpeechDeliveries.Whisper), CancellationToken.None);

        clip.DurationMs.ShouldBe(500, "12 000 samples at 24 kHz");
        clip.CostUsd.ShouldBe(0.00048m);
        var request = fake.Requests[0];
        request.Uri.ToString().ShouldBe("https://api.elevenlabs.io/v1/text-to-speech/pNInz6obpgDQGcFmaJgB?output_format=pcm_24000");
        request.Headers["xi-api-key"].ShouldBe("fake-test-key-voice-0123");
        var body = JsonNode.Parse(request.Body)!;
        body["text"]!.GetValue<string>().ShouldBe("[whispers] Selam.");
        body["model_id"]!.GetValue<string>().ShouldBe("eleven_v4");
        body["language_code"]!.GetValue<string>().ShouldBe("tr");

        await Should.ThrowAsync<SpeechException>(() => engine.SynthesizeAsync(new SpeechRequest("x", "tr", "", 1, Path.Combine(_folder, "f.wav"),
            Model("elevenlabs", "eleven_v4", 80m)), CancellationToken.None));
        JsonNode.Parse(ElevenLabsSpeechEngine.Body("x", "eleven_flash_v2_5", "tr", SpeechDeliveries.Laugh))!["text"]!.GetValue<string>()
            .ShouldBe("x", "Flash has no audio tags: the delivery is dropped");
        JsonNode.Parse(ElevenLabsSpeechEngine.Body("x", "eleven_multilingual_v2", "tr", ""))!.AsObject().ContainsKey("language_code").ShouldBeFalse();
    }

    [Fact]
    public async Task ChirpAsksForTheLocaleVoiceAndTheSpeed()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, new JsonObject { ["audioContent"] = Convert.ToBase64String(Wave(4800)) }.ToJsonString());
        using var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var engine = new GoogleCloudSpeechEngine(new GoogleCloudProvider(http, NullLogger<GoogleCloudProvider>.Instance), http);
        var clip = await engine.SynthesizeAsync(new SpeechRequest("Bir.", "tr", "Achernar", 1.2, Path.Combine(_folder, "c.wav"),
            Model("google-cloud", "chirp3-hd", 30m)), CancellationToken.None);

        clip.DurationMs.ShouldBe(200);
        var request = fake.Requests.Single();
        request.Uri.ToString().ShouldBe("https://texttospeech.googleapis.com/v1/text:synthesize");
        var body = JsonNode.Parse(request.Body)!;
        body["voice"]!["name"]!.GetValue<string>().ShouldBe("tr-TR-Chirp3-HD-Achernar");
        body["audioConfig"]!["speakingRate"]!.GetValue<double>().ShouldBe(1.2);
        engine.Supports("tr").ShouldBeTrue();
        engine.Supports("xx").ShouldBeFalse();
    }

    [Fact]
    public void DeliveryMarkersReachTheirPartAndSoundsComeOnce()
    {
        var parts = MarkerText.Parts("Düz başlar. [heyecanlı] Kazandık! Gerçekten. [gülerek] Ha.", out var ignored);
        ignored.ShouldBeEmpty();
        parts.Select(p => (p.Text, p.Delivery)).ShouldBe([("Düz başlar.", ""), ("Kazandık! Gerçekten.", SpeechDeliveries.Excited), ("Ha.", SpeechDeliveries.Laugh)]);
        MarkerText.Spoken("[fısıltı] Sessiz [sigh] ol.").ShouldBe("Sessiz ol.", "a delivery marker is never spoken or shown");
        MarkerCatalog.All.Where(m => m.Delivery.Length > 0).Select(m => m.Delivery).ShouldBe(SpeechDeliveries.All);
        SpeechDeliveries.IsSound(SpeechDeliveries.Sigh).ShouldBeTrue();
        SpeechDeliveries.IsSound(SpeechDeliveries.Sad).ShouldBeFalse();
    }

    [Fact]
    public void YazgiIsALocalTurkishVoiceOnTheProcessorWithPinnedFilesAndALock()
    {
        var voice = PythonVoices.YazgiTts;
        voice.Code.ShouldBe(SpeechEngines.YazgiTts);
        PythonVoices.All.ShouldContain(voice);
        PythonFiles.HasLock(PythonEnvironments.Yazgi, "cpu").ShouldBeTrue();
        PythonEnvironments.Yazgi.ProcessorOnly.ShouldBeTrue();
        voice.Files.Count.ShouldBe(12, "config, two weights, the package wheel and eight voices");
    }
}

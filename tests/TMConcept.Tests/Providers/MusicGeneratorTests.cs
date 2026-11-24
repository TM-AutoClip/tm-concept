using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using TMConcept.Providers.Music;

namespace TMConcept.Tests.Providers;

/// <summary>
/// The online music models' requests and answers (0.1.46, C5) from recorded examples, without a network: Lyria's body (model + prompt) and
/// its answer (the audio block of the model's output step, base64), ElevenLabs Music's body (prompt, length in ms within its bounds, model,
/// instrumental) and its answer (the MP3 itself; a JSON error is not audio).
/// </summary>
public sealed class MusicGeneratorTests
{
    // The shape of an Interactions answer as the music generation page shows it (a text block with the structure, then the audio).
    private const string LyriaAnswer = """
        {
          "id": "int-1",
          "steps": [
            { "type": "user_input", "content": [ { "type": "text", "text": "prompt" } ] },
            { "type": "model_output", "content": [
              { "type": "text", "text": "[Intro] soft strings" },
              { "type": "audio", "data": "SUQzAwAAAAAAAA==" }
            ] }
          ]
        }
        """;

    [Fact]
    public void LyriasBodyIsTheModelAndThePrompt()
    {
        var body = JsonNode.Parse(LyriaMusicGenerator.Body("lyria-3-clip-preview", "calm cinematic background music, no vocals"))!;

        body["model"]!.GetValue<string>().ShouldBe("lyria-3-clip-preview");
        body["input"]!.GetValue<string>().ShouldBe("calm cinematic background music, no vocals");
        body.AsObject().Count.ShouldBe(2, "no undocumented field is sent");
        LyriaMusicGenerator.ApiRevision.ShouldBe("2026-05-20");
    }

    [Fact]
    public void LyriasAnswerGivesTheAudioOfTheModelsOutput()
    {
        var audio = LyriaMusicGenerator.Audio(Encoding.UTF8.GetBytes(LyriaAnswer)).ShouldNotBeNull();

        Encoding.ASCII.GetString(audio, 0, 3).ShouldBe("ID3");
        LyriaMusicGenerator.Audio(Encoding.UTF8.GetBytes("""{"steps":[{"type":"model_output","content":[{"type":"text","text":"x"}]}]}""")).ShouldBeNull();
        LyriaMusicGenerator.Audio(Encoding.UTF8.GetBytes("""{"error":{"code":400}}""")).ShouldBeNull();
        LyriaMusicGenerator.Audio(Encoding.UTF8.GetBytes("not json")).ShouldBeNull();
        LyriaMusicGenerator.Audio(Encoding.UTF8.GetBytes("""{"steps":[{"type":"model_output","content":[{"type":"audio","data":"%%%"}]}]}""")).ShouldBeNull();
    }

    [Fact]
    public void ElevenLabsBodyAsksForTheLengthWithoutWords()
    {
        var body = JsonNode.Parse(ElevenLabsMusicGenerator.Body("music_v1", "epic orchestral", 45, instrumental: true))!;

        body["prompt"]!.GetValue<string>().ShouldBe("epic orchestral");
        body["music_length_ms"]!.GetValue<int>().ShouldBe(45_000);
        body["model_id"]!.GetValue<string>().ShouldBe("music_v1");
        body["force_instrumental"]!.GetValue<bool>().ShouldBeTrue();
        JsonNode.Parse(ElevenLabsMusicGenerator.Body("music_v1", "x", 1, true))!["music_length_ms"]!.GetValue<int>().ShouldBe(3_000, "the API's least");
        JsonNode.Parse(ElevenLabsMusicGenerator.Body("music_v1", "x", 900, true))!["music_length_ms"]!.GetValue<int>().ShouldBe(600_000, "the API's most");
    }

    [Fact]
    public void ElevenLabsAnswerMustBeAudio()
    {
        ElevenLabsMusicGenerator.IsAudio([(byte)'I', (byte)'D', (byte)'3', 4, 0]).ShouldBeTrue();
        ElevenLabsMusicGenerator.IsAudio([0xFF, 0xFB, 0x90, 0x00]).ShouldBeTrue("an MP3 frame without a tag");
        ElevenLabsMusicGenerator.IsAudio(Encoding.UTF8.GetBytes("""{"detail":[{"msg":"bad"}]}""")).ShouldBeFalse();
    }
}

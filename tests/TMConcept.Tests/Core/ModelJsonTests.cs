using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Images;

namespace TMConcept.Tests.Core;

/// <summary>Tolerant reading of model answers that must be JSON (weak local models: thinking, fences, prose, small mistakes).</summary>
public sealed class ModelJsonTests
{
    [Theory]
    [InlineData("{\"topics\":[\"a\",\"b\"]}")]
    [InlineData("<think>Let me think {about} it.</think>\n{\"topics\":[\"a\",\"b\"]}")]
    [InlineData("<thinking>x</thinking>```json\n{\"topics\":[\"a\",\"b\"]}\n```\nHope this helps!")]
    [InlineData("Sure! Here you go: {\"topics\":[\"a\",\"b\"]} Let me know if you want more {topics}.")]
    [InlineData("{\"topics\":[\"a\",\"b\",],}")]
    [InlineData("{\"topics\":[\"a\", // first\n\"b\"]}")]
    [InlineData("{\u201Ctopics\u201D: [\u201Ca\u201D, \u201Cb\u201D]}")]
    public void TheJsonIsFoundAndRead(string answer)
    {
        var json = ModelJson.TryParseObject(answer);

        json.ShouldNotBeNull();
        json["topics"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void RawLineBreaksInsideStringsAreRepaired()
    {
        var json = ModelJson.TryParseObject("{\"content\": \"line one\nline two\ttabbed\"}");

        json!["content"]!.GetValue<string>().ShouldBe("line one\nline two\ttabbed");
    }

    [Fact]
    public void BracesInsideStringsDoNotEndTheJson()
    {
        ModelJson.Extract("x {\"a\": \"} and ] and \\\" quote\"} tail").ShouldBe("{\"a\": \"} and ] and \\\" quote\"}");
        ModelJson.Extract("[1, [2, 3]] and {\"b\": 1}").ShouldBe("[1, [2, 3]]", "a bare array counts too");
    }

    [Fact]
    public void ACutAnswerIsNotComplete()
    {
        ModelJson.IsComplete("{\"prompts\": [\"a\", \"b").ShouldBeFalse();
        ModelJson.IsComplete("ok {\"prompts\": [\"a\"]} done").ShouldBeTrue();
        ModelJson.TryParse("{\"prompts\": [\"a\", \"b").ShouldBeNull();
        ModelJson.StringItems("{\"prompts\": [\"a\", \"b \\\"c\\\"\", \"cut", "prompts").ShouldBe(["a", "b \"c\""]);
    }

    [Fact]
    public void NothingReadableGivesNull()
    {
        ModelJson.TryParse(null).ShouldBeNull();
        ModelJson.TryParse("I cannot help with that.").ShouldBeNull();
        ModelJson.TryParse("<think>{\"topics\": [\"only thinking\"]} and the answer was cut").ShouldBeNull("an unclosed thinking block is not the answer");
        ModelJson.TryParse("{\"topics\": [\"a\" \"b\"]}").ShouldBeNull("a missing comma is not guessed");
    }

    [Fact]
    public void ImagePromptsSurviveTextAfterTheJson()
    {
        // 0.1.16 read from the first brace to the END of the answer: one sentence after the JSON lost every prompt.
        IReadOnlyList<string> visuals = ["a map", "a ship"];
        var prompts = ImagePromptBuilder.Parse("{\"prompts\": [\"old map of Byzantium\", \"a dromon at sea\"]}\nThese prompts follow the rules.", visuals);

        prompts.ShouldBe(["old map of Byzantium", "a dromon at sea"]);
    }

    [Fact]
    public void ACutPromptAnswerKeepsThePromptsBeforeTheCut()
    {
        IReadOnlyList<string> visuals = ["a map", "a ship", "a wall"];
        var problems = new List<string>();

        var prompts = ImagePromptBuilder.Parse("```json\n{\"prompts\": [\"old map\", \"a dromon\", \"the walls of", visuals, problems.Add);

        prompts.ShouldBe(["old map", "a dromon", "a wall"], "the cut third prompt falls back to its scene");
        problems.ShouldHaveSingleItem().ShouldContain("2 of 3");
        ImagePromptBuilder.Read("no json at all").ShouldBeEmpty();
        ImagePromptBuilder.Parse("no json at all", visuals).ShouldBe(visuals);
    }
}
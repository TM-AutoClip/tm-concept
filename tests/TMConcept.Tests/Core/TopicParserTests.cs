using Shouldly;
using TMConcept.Core.Concepts;

namespace TMConcept.Tests.Core;

public sealed class TopicParserTests
{
    private const int Max = TopicLimits.CountMax;

    [Fact]
    public void ACutOffAnswerGivesTheTopicsWhoseClosingQuoteArrived()
    {
        TopicSuggester.ParsePartial("{\"topics\": [\"Greek fire\", \"The \\\"Nika\\\" riots\", \"The fall of Treb", Max).ShouldBe(["Greek fire", "The \"Nika\" riots"]);
        TopicSuggester.ParsePartial("```json\n{\"topics\":[\"A siege\",", Max).ShouldBe(["A siege"]);
        TopicSuggester.ParsePartial("{\"topics\": [\"Greek fire\", \"greek  fire\", \"\", \"Iconoclasm\"", Max).ShouldBe(["Greek fire", "Iconoclasm"],
            "cleaned like a complete answer: no repeats, no empty ones");
        TopicSuggester.ParsePartial("{\"scope\": \"Byzantium, 330-1453\", \"topics\": [\"Greek fire\", \"Iconocl", Max).ShouldBe(["Greek fire"],
            "the scope written first is not a topic");
    }

    [Fact]
    public void NothingReadableGivesNoTopics()
    {
        TopicSuggester.ParsePartial(string.Empty, Max).ShouldBeEmpty();
        TopicSuggester.ParsePartial("{\"topi", Max).ShouldBeEmpty();
        TopicSuggester.ParsePartial("{\"topics\": [\"Greek fi", Max).ShouldBeEmpty();
        TopicSuggester.ParsePartial("{\"topics\": [42, \"Greek fire\"]", Max).ShouldBeEmpty("a non-string item ends the reading");
        TopicSuggester.ParsePartial("{\"scope\": \"Byzantium\", \"top", Max).ShouldBeEmpty("only the scope arrived");
    }

    [Fact]
    public void ACompleteAnswerGivesTheSameAsTheFullParser()
    {
        const string answer = "{\"scope\": \"Byzantine history\", \"topics\": [\"Greek fire\", \"Iconoclasm\"]}";
        TopicSuggester.Parse(answer, Max).ShouldBe(["Greek fire", "Iconoclasm"], "the scope is ignored");
        TopicSuggester.ParsePartial(answer, Max).ShouldBe(TopicSuggester.Parse(answer, Max));
    }

    [Fact]
    public void AtMostTheAskedNumberIsKept()
    {
        var answer = "{\"scope\": \"s\", \"topics\": [" + string.Join(",", Enumerable.Range(1, 120).Select(i => $"\"Topic {i}\"")) + "]}";
        TopicSuggester.Parse(answer, 5).ShouldBe(["Topic 1", "Topic 2", "Topic 3", "Topic 4", "Topic 5"]);
        TopicSuggester.Parse(answer, 100).Count.ShouldBe(100);
        TopicSuggester.ParsePartial(answer, 5).Count.ShouldBe(5);
        TopicSuggester.ParsePartial(answer, 100).Count.ShouldBe(100);
    }

    [Fact]
    public void TheCallBudgetIsSixteenThousandOrTheModelsLimit()
    {
        TopicSuggester.OutputFor(new TMConcept.Core.Models.CatalogModel { MaxOutputTokens = 128_000 }).ShouldBe(16_000);
        TopicSuggester.OutputFor(new TMConcept.Core.Models.CatalogModel { MaxOutputTokens = 8_000 }).ShouldBe(8_000);
        TopicSuggester.OutputFor(new TMConcept.Core.Models.CatalogModel()).ShouldBe(16_000);
    }

    [Fact]
    public void TheOutputEstimateGrowsWithTheCountAndFitsTheCall()
    {
        var roof = TopicSuggester.OutputFor(new TMConcept.Core.Models.CatalogModel { MaxOutputTokens = 128_000 });
        TopicSuggester.OutputExpectedFor(100).ShouldBeGreaterThan(TopicSuggester.OutputExpectedFor(10));
        TopicSuggester.OutputExpectedFor(100).ShouldBeLessThanOrEqualTo(roof, "100 topics stay one call");
        TopicSuggester.OutputExpectedFor(10).ShouldBeLessThanOrEqualTo(roof);
        TopicSuggester.OutputMinFor(100).ShouldBeLessThan(TopicSuggester.OutputExpectedFor(100));
        TopicSuggester.OutputExpectedFor(1_000).ShouldBe(TopicSuggester.OutputExpectedFor(TopicLimits.CountMax), "the count is kept within 5-100");
        TopicSuggester.Clamp(1).ShouldBe(TopicLimits.CountMin);
    }
}

using Shouldly;
using TMConcept.Core.Concepts;

namespace TMConcept.Tests.Core;

public sealed class TopicParserTests
{
    [Fact]
    public void ACutOffAnswerGivesTheTopicsWhoseClosingQuoteArrived()
    {
        TopicSuggester.ParsePartial("{\"topics\": [\"Greek fire\", \"The \\\"Nika\\\" riots\", \"The fall of Treb").ShouldBe(["Greek fire", "The \"Nika\" riots"]);
        TopicSuggester.ParsePartial("```json\n{\"topics\":[\"A siege\",").ShouldBe(["A siege"]);
        TopicSuggester.ParsePartial("{\"topics\": [\"Greek fire\", \"greek  fire\", \"\", \"Iconoclasm\"").ShouldBe(["Greek fire", "Iconoclasm"],
            "cleaned like a complete answer: no repeats, no empty ones");
    }

    [Fact]
    public void NothingReadableGivesNoTopics()
    {
        TopicSuggester.ParsePartial(string.Empty).ShouldBeEmpty();
        TopicSuggester.ParsePartial("{\"topi").ShouldBeEmpty();
        TopicSuggester.ParsePartial("{\"topics\": [\"Greek fi").ShouldBeEmpty();
        TopicSuggester.ParsePartial("{\"topics\": [42, \"Greek fire\"]").ShouldBeEmpty("a non-string item ends the reading");
    }

    [Fact]
    public void ACompleteAnswerGivesTheSameAsTheFullParser()
    {
        const string answer = "{\"topics\": [\"Greek fire\", \"Iconoclasm\"]}";
        TopicSuggester.ParsePartial(answer).ShouldBe(TopicSuggester.Parse(answer));
    }

    [Fact]
    public void TheCallBudgetIsSixteenThousandOrTheModelsLimit()
    {
        TopicSuggester.OutputFor(new TMConcept.Core.Models.CatalogModel { MaxOutputTokens = 128_000 }).ShouldBe(16_000);
        TopicSuggester.OutputFor(new TMConcept.Core.Models.CatalogModel { MaxOutputTokens = 8_000 }).ShouldBe(8_000);
        TopicSuggester.OutputFor(new TMConcept.Core.Models.CatalogModel()).ShouldBe(16_000);
    }
}

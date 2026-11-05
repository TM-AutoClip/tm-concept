using Shouldly;
using TMConcept.Core.Concepts;

namespace TMConcept.Tests.Core;

public sealed class ConceptResponseParserTests
{
    [Fact]
    public void ReadsTheSchemaInsideACodeFence()
    {
        var answer = "Here you go:\n```json\n{\"konseptler\":[{\"baslik\":\"A\",\"ozet\":\"S1. S2.\",\"icerik\":\"[x]\\nHello\"}]}\n```";
        var result = ConceptResponseParser.Parse(answer);
        result.Concepts.Single().ShouldBe(new ParsedConcept("A", "S1. S2.", "[x]\nHello"));
        result.Problems.ShouldBeEmpty();
        result.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void KeepsCompleteItemsOfACutOffAnswer()
    {
        var answer = "{\"konseptler\":[{\"baslik\":\"One\",\"ozet\":\"s\",\"icerik\":\"c\"},{\"baslik\":\"Two\",\"ozet\":\"s\",\"icerik\":\"c [x] }\"},{\"baslik\":\"Thr";
        var result = ConceptResponseParser.Parse(answer);
        result.Concepts.Select(c => c.Title).ShouldBe(["One", "Two"]);
        result.Truncated.ShouldBeTrue();
    }

    [Fact]
    public void SkipsABrokenItemAndRemovesNul()
    {
        var answer = "{\"concepts\":[{\"title\":\"\",\"summary\":\"s\",\"content\":\"c\"},{\"title\":\"Ok\\u0000\",\"summary\":\"s\",\"content\":\"c\"}]}";
        var result = ConceptResponseParser.Parse(answer);
        result.Concepts.Single().Title.ShouldBe("Ok");
        result.Problems.Count.ShouldBe(1);
    }

    [Fact]
    public void NoListIsAProblem() => ConceptResponseParser.Parse("sorry").Problems.ShouldNotBeEmpty();
}

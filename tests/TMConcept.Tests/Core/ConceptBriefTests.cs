using Shouldly;
using TMConcept.Core.Concepts;

namespace TMConcept.Tests.Core;

/// <summary>The two kinds of concept run (0.1.13): ticked topics or an idea with a count, never both.</summary>
public sealed class ConceptBriefTests
{
    [Fact]
    public void AnIdeaBriefKeepsItsCountWithinOneToAHundredAndTrimsTheIdea()
    {
        new IdeaBrief("x", 0).Count.ShouldBe(ConceptLimits.CountMin);
        new IdeaBrief("x", 150).Count.ShouldBe(ConceptLimits.CountMax);
        new IdeaBrief("x", 7).Count.ShouldBe(7);
        new IdeaBrief("  the last siege \n", 1).ExtraPrompt.ShouldBe("the last siege");
        new IdeaBrief(string.Empty, 1).ExtraPrompt.ShouldBeEmpty("an empty idea lets the channel alone drive the concepts");
        new IdeaBrief("x", 5).Topics(0, 5).ShouldBeNull("an idea never sends topics");
    }

    [Fact]
    public void ATopicBriefHasNoExtraRequestAndSlicesItsTopics()
    {
        var brief = new TopicBrief(["A", "B", "C", "D", "E"]);

        brief.Count.ShouldBe(5, "one concept per topic");
        brief.ExtraPrompt.ShouldBeEmpty();
        brief.Topics(0, 2).ShouldBe(["A", "B"]);
        brief.Topics(2, 2).ShouldBe(["C", "D"]);
        brief.Topics(4, 20).ShouldBe(["E"]);
    }
}

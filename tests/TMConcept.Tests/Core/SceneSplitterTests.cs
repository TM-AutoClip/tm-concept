using Shouldly;
using TMConcept.Core.Scenes;

namespace TMConcept.Tests.Core;

public sealed class SceneSplitterTests
{
    [Fact]
    public void BracketLinesStartScenesAndTextIsNarration()
    {
        var split = SceneSplitter.Split("[on screen: night, burning ships]\nIn 672 the city was besieged.\nThe fleet waited.\n[ekranda: harita]\nSonra geldi.");
        split.IsValid.ShouldBeTrue();
        split.Scenes.Count.ShouldBe(2);
        split.Scenes[0].Visual.ShouldBe("night, burning ships");
        split.Scenes[0].Narration.ShouldBe("In 672 the city was besieged. The fleet waited.");
        split.Scenes[1].Visual.ShouldBe("harita");
        split.Scenes[1].Number.ShouldBe(2);
    }

    [Fact]
    public void TextBeforeTheFirstBracketIsAScene()
    {
        var split = SceneSplitter.Split("Hook first.\n[map]\nThen more.");
        split.Scenes.Count.ShouldBe(2);
        split.Scenes[0].Visual.ShouldBeEmpty();
        split.Scenes[0].Narration.ShouldBe("Hook first.");
    }

    [Fact]
    public void InlineBracketsAreRemovedFromNarration()
    {
        var split = SceneSplitter.Split("[a]\nHello [aside] world.");
        split.Scenes[0].Narration.ShouldBe("Hello world.");
    }

    [Fact]
    public void EmptyOrVisualOnlyScriptIsInvalid()
    {
        SceneSplitter.Split("").IsValid.ShouldBeFalse();
        SceneSplitter.Split("[only a picture]").IsValid.ShouldBeFalse();
    }
}

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
    public void TheVoiceNeverReadsPastWhatTheSubtitlesKeep()
    {
        var plain = SceneSplitter.Split("[map]\n" + new string('a', SceneSplitter.MaxNarration + 300)).Scenes.Single();
        plain.Narration.Length.ShouldBe(SceneSplitter.MaxNarration);
        plain.Script.ShouldBe(plain.Narration, "without placeholders the voice reads exactly the narration");

        var marked = SceneSplitter.Split("[map]\n[pause 1 s] " + new string('b', SceneSplitter.MaxNarration + 300)).Scenes.Single();
        marked.Script.ShouldStartWith("[pause 1 s]");
        TMConcept.Core.Markers.MarkerText.Spoken(marked.Script).Length.ShouldBe(SceneSplitter.MaxNarration, "placeholders do not count, spoken text does");
    }

    [Fact]
    public void ATransitionAfterTheLastSceneIsIgnoredAndTheTextStaysUsable()
    {
        var split = SceneSplitter.Split("[map]\nThe end.\n[hard cut]");
        split.IsValid.ShouldBeTrue();
        split.Scenes.Single().Narration.ShouldBe("The end.");
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
    public void ATransitionPlaceholderIsNoSceneButHowTheSceneBegins()
    {
        var split = SceneSplitter.Split(
            "[on screen: a map]\nIn 672 the city was besieged.\n[sert geçiş]\n[on screen: burning ships]\nThe fleet waited.\n"
            + "[on screen: the walls] [soft transition]\nThey held.\n[on screen: dawn]\n[hard cut] Morning came.\n[on screen: the end]\nOver. [soft transition]");

        split.Scenes.Select(s => s.Visual).ShouldBe(["a map", "burning ships", "the walls", "dawn", "the end"], "a placeholder line is not a scene");
        split.Scenes.Select(s => s.Transition).ShouldBe(
            [SceneTransitions.None, SceneTransitions.Cut, SceneTransitions.Fade, SceneTransitions.Cut, SceneTransitions.Fade],
            "on a line of its own: the next scene; on a scene line or in its narration: that scene");
        split.Scenes[3].Narration.ShouldBe("Morning came.");
        split.Scenes[4].Narration.ShouldBe("Over.");
        SceneSplitter.Split("[soft transition] Hook first.\n[on screen: x]\nMore.").Scenes[0].Transition.ShouldBe(SceneTransitions.None, "the first scene has none");
        SceneSplitter.IsSceneLine("[sert geçiş]").ShouldBeFalse();
        SceneSplitter.IsSceneLine("[ekranda: harita]").ShouldBeTrue();
    }

    [Fact]
    public void NarrationPlaceholdersStayInTheScriptAndNeverInTheNarration()
    {
        var split = SceneSplitter.Split("[on screen: a map]\nThe city fell. [pause 1 s] Then [slow] silence.\n[duraklama 2 sn]\nAnd [aside] more.\n[on screen: b]\nPlain.");

        split.Scenes.Count.ShouldBe(2, "a pause on a line of its own is no scene");
        split.Scenes[0].Narration.ShouldBe("The city fell. Then silence. And more.");
        split.Scenes[0].Script.ShouldBe("The city fell. [pause 1 s] Then [slow] silence. [duraklama 2 sn] And more.");
        split.Scenes[1].Script.ShouldBe("Plain.");
    }

    [Fact]
    public void EmptyOrVisualOnlyScriptIsInvalid()
    {
        SceneSplitter.Split("").IsValid.ShouldBeFalse();
        SceneSplitter.Split("[only a picture]").IsValid.ShouldBeFalse();
    }
}

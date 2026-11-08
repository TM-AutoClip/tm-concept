using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Markers;
using TMConcept.Core.Profiles;
using TMConcept.Core.Scenes;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Tests.Core;

public sealed class MarkerTests
{
    [Fact]
    public void TheCatalogHoldsOnlyWhatTheProgramAppliesItself()
    {
        MarkerCatalog.All.Select(m => m.Id).ShouldBe(["pause-0.5", "pause-1", "pause-2", "slow", "fast", "normal-speed", "hard-cut", "soft-transition"],
            "no style request for a model, nothing that cannot be applied exactly (emphasis on screen, a big number…)");
        MarkerCatalog.Of(MarkerKind.Transition).Select(m => m.Transition).ShouldBe([SceneTransitions.Cut, SceneTransitions.Fade]);
        foreach (var marker in MarkerCatalog.All)
        {
            marker.Token.Tr.ShouldStartWith("[");
            marker.Token.En.ShouldEndWith("]");
            marker.Hint.Tr.ShouldNotBeEmpty();
            marker.Hint.En.ShouldNotBeEmpty();
            MarkerText.Find(marker.Token.Tr).ShouldBe(marker);
            MarkerText.Find(marker.Token.En).ShouldBe(marker);
            var applied = marker.Kind == MarkerKind.Narration ? marker.PauseMs > 0 || marker.Speed > 0 : marker.Transition.Length > 0;
            applied.ShouldBeTrue("every placeholder is applied on this computer");
        }
    }

    [Fact]
    public void NoPlaceholderGoesToTheConceptModel()
    {
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Calm documentary [soft transition]" };

        var prompt = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, [], [], "only empresses [pause 1 s]", 5));

        (prompt.System + prompt.User).ShouldNotContain("MARKERS");
        (prompt.System + prompt.User).ShouldNotContain("[hard cut]");
        var turkish = ConceptPromptBuilder.Build(new ConceptPromptInput(new Profile { Name = "B", ContentLanguage = ContentLanguages.Turkish, ConceptText = "x" },
            [], [], "y", 5));
        (turkish.System + turkish.User).ShouldNotContain("İŞARET");
    }

    [Fact]
    public void AMarkerGoesInAtTheCaretWithSpacesWhereAWordTouchesIt()
    {
        MarkerText.Insert("abc def", 3, 3, "[hard cut]").ShouldBe(("abc [hard cut] def", 14));
        MarkerText.Insert("abc", 3, 3, "[slow]").ShouldBe(("abc [slow]", 10));
        MarkerText.Insert("", 0, 0, "[slow]").ShouldBe(("[slow]", 6));
        MarkerText.Insert("say this here", 9, 4, "[fast]").ShouldBe(("say [fast] here", 11), "a selection is replaced");
    }

    [Fact]
    public void ANarrationTextBecomesSpokenPartsWithItsPausesAndSpeeds()
    {
        var parts = MarkerText.Parts("[duraklama 1 sn] The city fell. [pause 0,5 s][pause 0.5 s] Then [slow] silence [normal speed] came. [hard cut] [aside]",
            out var ignored);

        parts.ShouldBe(
        [
            new NarrationPart(string.Empty, 1, 1000),
            new NarrationPart("The city fell.", 1, 1000),
            new NarrationPart("Then", 1, 0),
            new NarrationPart("silence", MarkerCatalog.SlowSpeed, 0),
            new NarrationPart("came.", 1, 0),
        ]);
        ignored.ShouldBe(["[aside]"], "a transition belongs to the scene: left out without a word; other bracket text is not spoken either");
        MarkerText.Spoken("The city fell. [pause 1 s] Then [slow] silence [soft transition]").ShouldBe("The city fell. Then silence");
        MarkerText.Silence("a [pause 1 s] b [duraklama 0,5 sn] [slow]").ShouldBe(1500);
        MarkerText.Transition("[sert geçiş] text [Yumuşak Geçiş]").ShouldBe(SceneTransitions.Fade, "the last one wins, any letter case");
        MarkerText.Transition("no placeholder [aside]").ShouldBe(SceneTransitions.None);
    }

    [AvaloniaFact]
    public void ThePickerPutsTheMarkerIntoItsTextBoxAtTheCaret()
    {
        var box = new TextBox { Text = "The city fell. Then silence" };
        var picker = new MarkerPicker { Target = box };
        var window = new Window { Content = new StackPanel { Children = { box, picker } } };
        window.Show();
        box.CaretIndex = "The city fell.".Length;

        picker.Insert(new MarkerOption("[duraklama 1 sn]", "araya sessizlik eklenir"));

        box.Text.ShouldBe("The city fell. [duraklama 1 sn] Then silence");
        box.CaretIndex.ShouldBe("The city fell. [duraklama 1 sn] ".Length - 1);
        window.Close();
    }
}

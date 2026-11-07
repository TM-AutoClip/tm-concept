using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Markers;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Tests.Core;

public sealed class MarkerTests
{
    [Fact]
    public void BothSpellingsAreFoundInAnyCaseAndTheLegendSpeaksThePromptsLanguage()
    {
        var used = MarkerText.Used(MarkerKind.Prompt, "Bizans [Sert Geçiş] olsun", "keep it [hard cut], add [emphasis] and [unknown]");

        used.Select(m => m.Id).ShouldBe(["hard-cut", "emphasis"]);
        var english = MarkerText.Legend(ContentLanguages.English, "[sert geçiş]")!;
        english.ShouldContain("[sert geçiş] / [hard cut] = scenes change with hard cuts");
        MarkerText.Legend(ContentLanguages.Turkish, "[hard cut]")!.ShouldContain("sert kesmeyle");
        MarkerText.Legend(ContentLanguages.English, "no marker here", null).ShouldBeNull();
        MarkerText.Used(MarkerKind.Prompt, "[pause 1 s]").ShouldBeEmpty("a narration marker is not a prompt marker");
    }

    [Fact]
    public void TheConceptPromptExplainsOnlyTheMarkersItsTextsUse()
    {
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Calm documentary [map]" };

        var withMarkers = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, [], [], "only empresses [ending question]", 5));
        var without = ConceptPromptBuilder.Build(new ConceptPromptInput(profile, [], [], "only empresses", 5));

        withMarkers.User.ShouldContain("MARKERS in the « » texts above");
        withMarkers.User.ShouldContain("[harita] / [map] = use map scenes");
        withMarkers.User.ShouldContain("[sonda soru] / [ending question] = end the video with a question to the viewer.");
        withMarkers.System.ShouldNotContain("MARKERS", Case.Sensitive, "the legend is in the changing part, after the extra request");
        without.User.ShouldContain("[harita] / [map]", Case.Sensitive, "the channel concept's marker is still explained");
        ConceptPromptBuilder.Build(new ConceptPromptInput(new Profile { ConceptText = "plain" }, [], [], "plain", 5)).User.ShouldNotContain("MARKERS");
    }

    [Fact]
    public void AMarkerGoesInAtTheCaretWithSpacesWhereAWordTouchesIt()
    {
        MarkerText.Insert("abc def", 3, 3, "[hard cut]").ShouldBe(("abc [hard cut] def", 14));
        MarkerText.Insert("abc", 3, 3, "[map]").ShouldBe(("abc [map]", 9));
        MarkerText.Insert("", 0, 0, "[map]").ShouldBe(("[map]", 5));
        MarkerText.Insert("say this here", 9, 4, "[emphasis]").ShouldBe(("say [emphasis] here", 15), "a selection is replaced");
    }

    [Fact]
    public void ANarrationTextBecomesSpokenPartsWithItsPausesAndSpeeds()
    {
        var parts = MarkerText.Parts("[duraklama 1 sn] The city fell. [pause 0,5 s][pause 0.5 s] Then [slow] silence [normal speed] came. [hard cut]",
            out var ignored);

        parts.ShouldBe(
        [
            new NarrationPart(string.Empty, 1, 1000),
            new NarrationPart("The city fell.", 1, 1000),
            new NarrationPart("Then", 1, 0),
            new NarrationPart("silence", MarkerCatalog.SlowSpeed, 0),
            new NarrationPart("came.", 1, 0),
        ]);
        ignored.ShouldBe(["[hard cut]"], "a visual marker needs new code; it is not spoken");
        MarkerText.Spoken("The city fell. [pause 1 s] Then [slow] silence").ShouldBe("The city fell. Then silence");
    }

    [Fact]
    public void EveryMarkerHasBothSpellingsAHintAndPromptMarkersAMeaning()
    {
        foreach (var marker in MarkerCatalog.All)
        {
            marker.Token.Tr.ShouldStartWith("[");
            marker.Token.En.ShouldEndWith("]");
            marker.Hint.Tr.ShouldNotBeEmpty();
            marker.Hint.En.ShouldNotBeEmpty();
            MarkerText.Find(marker.Token.Tr).ShouldBe(marker);
            MarkerText.Find(marker.Token.En).ShouldBe(marker);
            if (marker.Kind == MarkerKind.Prompt)
            {
                (marker.Meaning.Tr.Length > 0 && marker.Meaning.En.Length > 0).ShouldBeTrue();
            }
            else
            {
                (marker.PauseMs > 0 || marker.Speed > 0).ShouldBeTrue("a narration marker is applied on this computer");
            }
        }
    }

    [AvaloniaFact]
    public void ThePickerPutsTheMarkerIntoItsTextBoxAtTheCaret()
    {
        var box = new TextBox { Text = "only empresses please" };
        var picker = new MarkerPicker { Target = box };
        var window = new Window { Content = new StackPanel { Children = { box, picker } } };
        window.Show();
        box.CaretIndex = "only empresses".Length;

        picker.Insert(new MarkerOption("[vurgu]", "[vurgu] — ana fikir"));

        box.Text.ShouldBe("only empresses [vurgu] please");
        box.CaretIndex.ShouldBe("only empresses [vurgu] ".Length - 1);
        window.Close();
    }
}

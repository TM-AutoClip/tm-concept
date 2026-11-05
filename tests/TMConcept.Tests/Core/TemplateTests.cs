using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Templates;

namespace TMConcept.Tests.Core;

public sealed class TemplateTests
{
    [Fact]
    public void FourTemplatesWithBothLanguages()
    {
        var all = new EmbeddedTemplateSource().All;
        all.Select(t => t.Id).ShouldBe(["history.byzantine", "history.ottoman", "technology.ancient", "technology.modern"]);
        foreach (var t in all)
        {
            t.Concept.Tr.ShouldNotBeNullOrWhiteSpace();
            t.Concept.En.ShouldNotBeNullOrWhiteSpace();
            t.ExtraPromptExamples.Tr.Count.ShouldBe(3);
            t.ExtraPromptExamples.En.Count.ShouldBe(3);
        }
    }

    [Fact]
    public void ApplyingFillsTheProfileInItsLanguageAndNeverChangesTheTemplate()
    {
        var source = new EmbeddedTemplateSource();
        var template = source.Find("technology.modern")!;
        var profile = new Profile { ContentLanguage = ContentLanguages.Turkish, SpeechVoice = "3" };
        TemplateApplier.Apply(template, profile);
        profile.Name.ShouldBe("İçinde Ne Var?");
        profile.TargetSeconds.ShouldBe(60);
        profile.SpeechVoice.ShouldBe("3");

        profile.ConceptText = "changed by the user";
        template.Concept.Tr = "changed copy";
        source.Find("technology.modern")!.Concept.Tr.ShouldStartWith("Hayal:");
    }
}

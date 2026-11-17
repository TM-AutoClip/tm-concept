using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Desktop.Catalog;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;
using TMConcept.Speech.Python;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The "?" notes of the models (0.1.23): built only from the catalog and the engines' own facts (Turkish, licence, graphics card class, own
/// voices, size), never a judgement; a fact that is not known is left out.
/// </summary>
public sealed class ModelNotesTests
{
    private static readonly string[] Judgements = ["best", "better", "recommended", "quality", "great", "en iyi", "daha iyi", "önerilen", "kalite", "harika"];

    [Fact]
    public void AVoiceSaysItsTurkishItsLicenceItsCardClassAndItsOwnVoices()
    {
        var text = Turkish();
        var notes = new ModelNotes(text);
        var xtts = new CatalogModel
        {
            Provider = ModelProviders.Local, Id = PythonVoices.XttsV2.Code, Name = "XTTS-v2", Capabilities = ModelCapability.Speech, MinVideoMemoryGb = 4,
            License = "cpml", LicenseUse = LicenseUse.NonCommercial,
        };
        var facts = ModelFactLookup.For(xtts, new LanguageVoice([ContentLanguages.Turkish, ContentLanguages.English]), 8L << 30, noCard: false);

        var lines = notes.Lines(xtts, facts);

        lines.ShouldContain(text["notes.where.local"]);
        lines.ShouldContain(text["notes.turkish.yes"]);
        lines.ShouldContain(text.Format("notes.license.noncommercial", text["license.cpml"]));
        lines.ShouldContain(text.Format("notes.card.class", 4));
        lines.ShouldContain(text.Format("notes.card.fits", "8"));
        lines.ShouldContain(text["notes.voice.own"], "the XTTS-v2 catalog says it speaks with the user's own samples");
        facts.DownloadBytes.ShouldBe(PythonVoices.XttsV2.Files.Sum(f => f.ApproximateBytes));
        lines.ShouldNotContain(l => l.StartsWith(text.Format("notes.price.speech", string.Empty), StringComparison.Ordinal), "a local voice has no price");
    }

    [Fact]
    public void AVoiceWithoutTurkishSaysSoAndATooSmallCardIsNamed()
    {
        var text = Turkish();
        var model = new CatalogModel
        {
            Provider = ModelProviders.Local, Id = "some-voice", Name = "Some voice", Capabilities = ModelCapability.Speech, MinVideoMemoryGb = 12,
            License = "apache-2.0",
        };
        var lines = new ModelNotes(text).Lines(model, ModelFactLookup.For(model, new LanguageVoice([ContentLanguages.English]), 6L << 30, noCard: false));

        lines.ShouldContain(text["notes.turkish.no"]);
        lines.ShouldContain(text.Format("notes.license.commercial", text["license.apache-2.0"]));
        lines.ShouldContain(text.Format("notes.card.too_big", "6"));
        lines.ShouldNotContain(text["notes.voice.own"], "the catalog says nothing about own voices for it: left out");
        lines.ShouldNotContain(text["notes.voice.builtin"]);
    }

    [Fact]
    public void AnUnknownFactIsLeftOutNeverGuessed()
    {
        var text = new Localizer();
        var online = new CatalogModel { Provider = "openai", Id = "gpt-x", Name = "GPT X", Capabilities = ModelCapability.Chat, InputUsd = 1.25m, OutputUsd = 10m };

        var lines = new ModelNotes(text).Lines(online, ModelFacts.None);

        lines.ShouldBe([text.Format("notes.where.online", "openai"), text.Format("notes.price.text", text.Usd(1.25m), text.Usd(10m))]);
    }

    [Fact]
    public void EveryCatalogModelGetsOnlyFactLinesAndNoJudgement()
    {
        using var h = new DesktopHarness();
        foreach (var language in new[] { "en", "tr" })
        {
            var text = new Localizer();
            text.Use(language);
            var notes = new ModelNotes(text);
            var templates = Localizer.Keys("en").Where(k => k.StartsWith("notes.", StringComparison.Ordinal) && !k.StartsWith("notes.tip.", StringComparison.Ordinal))
                .Select(k => Template(text[k])).ToList();
            var models = h.Get<IModelCatalog>().All;
            models.Count.ShouldBeGreaterThan(10);
            foreach (var model in models)
            {
                var engine = h.Get<SpeechService>().EngineOf(model);
                var lines = notes.Lines(model, ModelFactLookup.For(model, model.Capabilities.HasFlag(ModelCapability.Speech) ? engine : null, 8L << 30, false));
                lines.ShouldNotBeEmpty();
                foreach (var line in lines)
                {
                    templates.Any(t => t.IsMatch(line)).ShouldBeTrue($"{model.Provider}/{model.Id}: '{line}' is not a catalog fact line");
                    Judgements.Any(j => Regex.IsMatch(line, @"\b" + j + @"\b", RegexOptions.IgnoreCase)).ShouldBeFalse($"{model.Id}: '{line}'");
                }
            }
        }
    }

    [Fact]
    public void TheListTipNamesTheEntriesThatShareUsefulFacts()
    {
        var text = Turkish();
        var voice = new LanguageVoice([ContentLanguages.Turkish]);
        CatalogModel Model(string id, string license, LicenseUse use, int card) => new()
        {
            Provider = ModelProviders.Local, Id = id, Name = id, Capabilities = ModelCapability.Speech, License = license, LicenseUse = use, MinVideoMemoryGb = card,
        };
        var copies = Model(PythonVoices.XttsV2.Code, "cpml", LicenseUse.NonCommercial, 4);
        var open = Model(PythonVoices.Trendyol.Code, "mit", LicenseUse.Commercial, 8);
        var big = Model("big", "apache-2.0", LicenseUse.Commercial, 24);
        var entries = new[] { copies, open, big }.Select(m => (m, ModelFactLookup.For(m, voice, 8L << 30, false))).ToList();

        var tip = new ModelNotes(text).ListTip(entries, ContentLanguages.Turkish).Split('\n');

        tip.ShouldContain(text.Format("notes.tip.own_voice", copies.Name));
        tip.ShouldContain(text.Format("notes.tip.commercial", open.Name + ", " + big.Name));
        tip.ShouldContain(text.Format("notes.tip.all.language", "Türkçe", open.Name), "Turkish, commercial use and an 8 GB card: only the MIT voice of the 8 GB class");
        new ModelNotes(text).ListTip([], ContentLanguages.Turkish).ShouldBeEmpty();
    }

    [Fact]
    public void AnAppOllamaModelSaysItsContextImagesAndThinking()
    {
        var text = new Localizer();
        var model = TMConcept.Core.AppOllama.AppModelCatalog.Models.First(m => m.Vision);

        var notes = new ModelNotes(text).Text(model).Split('\n');

        notes.ShouldContain(text["notes.vision"]);
        notes.ShouldContain(text[model.CanThink ? "notes.thinks" : "notes.thinks.no"]);
        notes.ShouldContain(text.Format("notes.context", model.ContextTokens.ToString("N0", text.Culture)));
    }

    [AvaloniaFact]
    public async Task ThePickersShowTheNotesOfEveryModelAndAListTip()
    {
        using var h = new DesktopHarness(s => s.AddSingleton<IGpuInfo>(new FakeGpu(8L << 30)));
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(null, ContentLanguages.Turkish, [PipelineSteps.Images, PipelineSteps.Speech]);

        foreach (var row in picker.Rows)
        {
            row.Options.Where(o => o.Model is not null).ShouldAllBe(o => o.Notes.Length > 0);
            row.Options.Where(o => o.Model is null).ShouldAllBe(o => o.Notes.Length == 0);
            row.ListTip.ShouldNotBeEmpty(row.Step.Code);
        }

        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Options.Where(o => o.Model is not null).ShouldAllBe(o => o.Notes.Contains(text["notes.turkish.yes"]), "every voice offered for a Turkish profile speaks Turkish");
        speech.ListTip.ShouldContain(text.Format("notes.tip.speaks", OutputLanguages.NameIn(ContentLanguages.Turkish, text.Language), string.Empty).Trim());
        var images = picker.Row(PipelineSteps.Images)!;
        images.Options.Where(o => o.Model is { Provider: ModelProviders.Local, MinVideoMemoryGb: > 0 }).ShouldAllBe(o => o.Notes.Contains(text.Format("notes.card.class", o.Model!.MinVideoMemoryGb)));
    }

    private static Localizer Turkish()
    {
        var text = new Localizer();
        text.Use("tr");
        return text;
    }

    /// <summary>A line template ("Licence {0}: commercial use allowed") as a pattern: the placeholders match anything.</summary>
    private static Regex Template(string template) =>
        new("^" + string.Join(".+", Regex.Split(template, @"\{\d\}").Select(Regex.Escape)) + "$", RegexOptions.Singleline);
}

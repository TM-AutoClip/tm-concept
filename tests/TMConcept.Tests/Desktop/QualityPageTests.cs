using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Engines;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;
using static TMConcept.Tests.Desktop.VideoPageTests;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.50 (K16) through the video page with a fake model and engine (no request leaves the computer): a Fast template is made exactly as before
/// even with a reviewer chosen, a Standard one is reviewed once and scored once and keeps its report and its two cost lines.
/// </summary>
public sealed class QualityPageTests
{
    private const string Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: burning ships]\nA strange fire burned on the water.";

    private const string PlanAnswer = """
        {"scenes":[
          {"scene":1,"base":"title","title":"672","region":[],"nodes":[],"links":[],"anchors":[],"beats":[{"sentence":1,"focus":"","zoom":1,"actions":[]}]},
          {"scene":2,"base":"diagram","title":"","region":[],"nodes":[{"id":"fire","label":"Fire","x":0.5,"y":0.4}],"links":[],"anchors":[],
           "beats":[{"sentence":1,"focus":"","zoom":1,"actions":[]}]}
        ]}
        """;

    private static ChatResponse Answer(string text) => new(text, new ChatUsage(900, 0, 0, 300), null, "end_turn", "r");

    private sealed class FakeSheet : IPlanSheet
    {
        public int Sheets { get; private set; }

        public Task<bool> SheetAsync(string video, IReadOnlyList<double> seconds, int columns, string png, CancellationToken cancellationToken)
        {
            Sheets++;
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            File.WriteAllBytes(png, PlaceholderImage.Png(8, 8));
            return Task.FromResult(true);
        }
    }

    private static DesktopHarness Harness(FakeChat chat, FakeEngine engine, FakeSheet sheet) => new(s =>
    {
        s.AddSingleton<IChatClientFactory>(chat);
        s.RemoveAll<IVideoEngine>();
        s.AddSingleton<IVideoEngine>(engine);
        s.AddSingleton<IMediaTools, FakeMedia>();
        s.AddSingleton<IPlanSheet>(sheet);
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
    });

    private static async Task<(VideoViewModel Page, Concept Concept)> OpenAsync(DesktopHarness h, QualityMode mode)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-quality-4567" }, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        foreach (var (step, model) in new[] { (PipelineSteps.VideoCode, "claude-sonnet-5-5"), (PipelineSteps.QualityCheck, "claude-haiku-5-5") })
        {
            await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = step, ProfileId = profile.Id, Provider = "anthropic", Model = model },
                CancellationToken.None);
        }

        var style = new VideoStyle { Name = "Drawn", Drawer = SceneDrawer.App, Quality = mode };
        await h.Get<IVideoStyleStore>().SaveAsync(style, CancellationToken.None);
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        page.SelectedStyle = page.Styles.Single(s => s.Id == style.Id);
        await page.EnqueueAsync(page.Approved.Single());
        return (page, concept);
    }

    [AvaloniaFact]
    public async Task AFastTemplateIsMadeAsBeforeEvenWithAReviewerChosen()
    {
        var engine = new FakeEngine();
        var sheet = new FakeSheet();
        var chat = new FakeChat().Answer(_ => Answer(PlanAnswer));
        using var h = Harness(chat, engine, sheet);
        var (page, concept) = await OpenAsync(h, QualityMode.Fast);
        page.Picker.Model(PipelineSteps.QualityCheck).ShouldNotBeNull();

        await page.ProduceAllAsync();

        chat.Requests.ShouldHaveSingleItem("one plan call, as before");
        sheet.Sheets.ShouldBe(0);
        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldHaveSingleItem();
        video.Warnings.ShouldNotContain("quality");
        var folder = Path.Combine(h.Get<IAppPaths>().LibraryDirectory, video.Folder);
        Directory.Exists(Path.Combine(folder, VideoFolders.Quality)).ShouldBeFalse();
        (await h.Get<IVideoStore>().CostsAsync(video.Id, CancellationToken.None)).ShouldNotContain(c => c.Step.StartsWith("quality", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task AStandardTemplateIsReviewedOnceScoredOnceAndKeepsItsReport()
    {
        var engine = new FakeEngine();
        var sheet = new FakeSheet();
        var chat = new FakeChat()
            .Answer(_ => Answer(PlanAnswer))
            .Answer(_ => Answer("{\"findings\":[{\"scene\":2,\"severity\":\"blocker\",\"problem\":\"The label is cut off.\"}]}"))
            .Answer(_ => Answer("{\"scores\":[{\"heading\":\"Readable text\",\"score\":3,\"note\":\"Small labels.\"}]}"));
        using var h = Harness(chat, engine, sheet);
        var (page, concept) = await OpenAsync(h, QualityMode.Standard);
        page.QueueEstimateTip.ShouldContain(h.Get<TMConcept.Desktop.Localization.Localizer>()["produced.cost.quality-critic"], Case.Sensitive,
            "the estimate names the critic's line");

        await page.ProduceAllAsync();

        chat.Requests.Count.ShouldBe(3, "the plan, one review, one critic: no repair");
        chat.Requests[1].Images.ShouldNotBeNull().Count.ShouldBe(1);
        chat.Requests[2].Images.ShouldNotBeNull().Count.ShouldBe(1);
        engine.Renders.ShouldBe(1, "a report only: nothing rendered again");
        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldHaveSingleItem();
        video.Warnings.Split(',').ShouldContain(VideoWarnings.QualityBlocker);
        video.Warnings.Split(',').ShouldContain(VideoWarnings.QualityFails);
        var folder = Path.Combine(h.Get<IAppPaths>().LibraryDirectory, video.Folder);
        var report = (await VideoAssembler.ReadQualityAsync(folder, CancellationToken.None)).ShouldNotBeNull();
        report.Mode.ShouldBe(QualityMode.Standard);
        report.Frames.ShouldHaveSingleItem().Findings.ShouldHaveSingleItem().Scene.ShouldBe(2);
        report.Critic.ShouldHaveSingleItem().Fails.ShouldBeTrue();
        File.Exists(Path.Combine(folder, VideoFolders.Quality, "sheet-1.png")).ShouldBeTrue();
        var costs = await h.Get<IVideoStore>().CostsAsync(video.Id, CancellationToken.None);
        costs.ShouldContain(c => c.Step == PipelineSteps.QualityCheck && c.Exact && c.Usd > 0);
        costs.ShouldContain(c => c.Step == PipelineSteps.QualityCriticCost && c.Exact && c.Usd > 0);
        costs.ShouldNotContain(c => c.Step == PipelineSteps.QualityRepairCost);
    }
}

using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Components;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Tools;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

public sealed class VideoPageTests
{
    private const string Content = "[on screen: a map]\nIn 672 the city was under siege.\n[on screen: burning ships]\nA strange fire burned on the water.";

    private static ChatResponse Code(string code) => new("```js\n" + code + "\n```", new ChatUsage(500, 0, 0, 800), null, "end_turn", "r");

    private static async Task<(VideoViewModel Page, Concept Concept)> PrepareAsync(DesktopHarness h, Action<VideoViewModel>? options = null)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" }, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice
        {
            Step = PipelineSteps.VideoCode,
            ProfileId = profile.Id,
            Provider = "anthropic",
            Model = "claude-sonnet-5-5",
        }, CancellationToken.None);
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        options?.Invoke(page);
        await page.EnqueueAsync(page.Approved.Single());
        return (page, concept);
    }

    private static DesktopHarness Harness(FakeChat chat, FakeEngine engine, Action<IServiceCollection>? more = null) => new(s =>
    {
        s.AddSingleton<IChatClientFactory>(chat);
        s.RemoveAll<IVideoEngine>();
        s.AddSingleton<IVideoEngine>(engine);
        s.AddSingleton<IMediaTools, FakeMedia>();
        more?.Invoke(s);
    });

    [AvaloniaFact]
    public async Task AQueuedConceptBecomesASilentVideoInTheLibrary()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);
        page.Queue.Count.ShouldBe(1);
        page.Approved.ShouldBeEmpty();

        await page.ProduceAllAsync();

        page.Queue.ShouldBeEmpty();
        page.Produced.Single().ShouldBe("Greek fire");
        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        video.Warnings.ShouldContain(VideoWarnings.SpeechOff);
        video.TotalUsd.ShouldBeGreaterThan(0);
        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, video.Folder);
        Path.GetFileName(folder).ShouldBe(VideoFolders.FirstVersion, "a video is a version folder inside its concept's folder");
        File.Exists(Path.Combine(folder, "video.mp4")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(folder, VideoFolders.Source, engine.CodeFile)).Trim().ShouldBe(engine.SampleCode, "the model's code as it rendered");
        File.Exists(Path.Combine(folder, VideoFolders.Source, "input.json")).ShouldBeTrue();
        Directory.Exists(Path.Combine(folder, VideoFolders.Source, "tmp")).ShouldBeFalse("the engine's temporary files are not kept");
        Directory.Exists(Path.Combine(folder, VideoFolders.Audio)).ShouldBeFalse("a silent video has no clips");
        File.ReadAllText(Path.Combine(folder, "subtitles.srt")).ShouldContain("siege");
        File.ReadAllText(Path.Combine(folder, "narration.txt")).ShouldContain("Scene 2");
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Produced);
        engine.Input!.Scenes.Count.ShouldBe(2);

        // No activity panel: the notification says the video is ready, what it cost and that it is silent.
        var toast = h.Get<TMConcept.Desktop.Notifications.ToastNotifier>().Open.Last().DataContext as TMConcept.Desktop.Notifications.ToastViewModel;
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        toast!.IsWarning.ShouldBeTrue();
        toast.Message.ShouldContain(text.Format("video.silent.off", "Greek fire"));
        toast.Message.ShouldContain(text.Usd(video.TotalUsd));
    }

    [AvaloniaFact]
    public async Task AVoicedVideoKeepsItsSceneClipsAndTheNarrationText()
    {
        var engine = new FakeEngine();
        var voice = new FakeVoice();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine, s =>
        {
            s.RemoveAll<TMConcept.Core.Speech.ISpeechEngine>();
            s.AddSingleton<TMConcept.Core.Speech.ISpeechEngine>(voice);
        });
        var (_, concept) = await PrepareAsync(h);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local,
            Model = voice.Code }, CancellationToken.None);

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = true });

        var folder = LibraryPaths.Resolve(h.Get<IAppPaths>().LibraryDirectory, record.Folder);
        Directory.GetFiles(Path.Combine(folder, VideoFolders.Audio)).Select(Path.GetFileName).ShouldBe(["1.wav", "2.wav"], ignoreOrder: true);
        File.ReadAllText(Path.Combine(folder, VideoFolders.NarrationFile)).ShouldContain("strange fire");
        File.ReadAllText(Path.Combine(folder, VideoFolders.Source, "input.json")).ShouldContain("siege");
        record.Warnings.ShouldNotContain(VideoWarnings.SpeechFailed);
    }

    [AvaloniaFact]
    public async Task AFailedVideoLeavesNoVersionFolderBehind()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine, s =>
        {
            s.RemoveAll<IMediaTools>();
            s.AddSingleton<IMediaTools>(new FakeMedia { FailThumbnail = true });
        });
        var (_, concept) = await PrepareAsync(h);

        (await Should.ThrowAsync<VideoException>(() => RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false }))).Code.ShouldBe("files");

        var profileFolder = Path.Combine(h.Get<IAppPaths>().LibraryDirectory, "Byzantium");
        (Directory.Exists(profileFolder) ? Directory.GetDirectories(profileFolder) : []).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task RejectedCodeGetsOneRepair()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat().Answer(_ => Code("fetch('x')")).Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, concept) = await PrepareAsync(h);

        await page.ProduceAllAsync();

        chat.Requests.Count.ShouldBe(2);
        chat.Requests[1].User.ShouldContain("network request");
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().Warnings.ShouldContain(VideoWarnings.Repaired);
    }

    [AvaloniaFact]
    public async Task ARenderThatFailsTwiceStaysInTheQueueWithItsError()
    {
        var engine = new FakeEngine { FailRenders = 2 };
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine);
        var (page, concept) = await PrepareAsync(h);

        await page.ProduceAllAsync();

        page.Queue.Single().Failed.ShouldBeTrue();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).ShouldBeEmpty();

        // Two code calls were paid (800 output tokens each): the row says what the failure cost, the queue item keeps it for a restart,
        // and the month's spending counts it as a failed video.
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var model = h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;
        var paid = 2 * h.Get<TMConcept.Core.Costs.ICostCalculator>().Exact(model, new ChatUsage(500, 0, 0, 800));
        page.Queue.Single().Error.ShouldEndWith(text.Format("video.failed.spent", text.Usd(paid)));
        var item = (await h.Get<IQueueStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        (item.LastErrorCode, item.FailedUsd).ShouldBe(("render", paid));
        var lost = (await h.Get<TMConcept.Core.Runs.IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None))
            .Single(r => r.Kind == TMConcept.Core.Runs.RunKind.FailedVideo);
        (lost.ActualUsd, lost.Status, lost.Error).ShouldBe((paid, TMConcept.Core.Runs.RunStatus.Failed, "render"));
        var spending = await TMConcept.Desktop.Localization.PriceInfo.SpendingAsync(h.Get<TMConcept.Core.Runs.IRunStore>(), h.Get<IProfileStore>(),
            h.Get<IVideoStore>(), TimeProvider.System, text, CancellationToken.None);
        spending.ShouldContain(text.Format("models.spending.failed", text.Usd(paid)));

        // A second failure adds to what the item lost.
        engine.FailRenders = 2;
        chat.Answer(_ => Code(engine.SampleCode)).Answer(_ => Code(engine.SampleCode));
        await page.ProduceAllAsync();
        (await h.Get<IQueueStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().FailedUsd.ShouldBe(2 * paid);
        page.Queue.Single().Error.ShouldEndWith(text.Format("video.failed.spent", text.Usd(2 * paid)));
    }

    [AvaloniaFact]
    public async Task AMadeVideoCarriesItsConceptShareOutsideItsOwnTotal()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (_, concept) = await PrepareAsync(h);

        // The concept came from a run of 0.11 $ that kept 5 concepts.
        var run = new TMConcept.Core.Runs.Run { ProfileId = concept.ProfileId, Kind = TMConcept.Core.Runs.RunKind.Concepts, ActualUsd = 0.11m, Accepted = 5,
            CostExact = true, Status = TMConcept.Core.Runs.RunStatus.Succeeded };
        await h.Get<TMConcept.Core.Runs.IRunStore>().AddAsync(run, CancellationToken.None);
        var generated = new Concept { ProfileId = concept.ProfileId, Title = "Iconoclasm", Summary = "s", Content = Content, Language = "en",
            Status = ConceptStatus.Queued, RunId = run.Id };
        await h.Get<IConceptStore>().AddAsync([generated], CancellationToken.None);
        var record = await RunAsync(h, generated, new VideoOptions { Engine = engine.Kind, Speech = false });

        var costs = await h.Get<IVideoStore>().CostsAsync(record.Id, CancellationToken.None);
        var share = costs.Single(c => c.Step == PipelineSteps.Concepts);
        (share.Usd, share.RunId, share.Exact).ShouldBe((0.022m, (Guid?)run.Id, true));
        record.TotalUsd.ShouldBe(costs.Where(c => c.Step != PipelineSteps.Concepts).Sum(c => c.Usd));
    }

    [AvaloniaFact]
    public async Task ACutOffVideoCodeSaysSoWithWhatWasPaidAndTheCallUsesTheWholeBudgetAndStreams()
    {
        var engine = new FakeEngine();
        var charged = new ChatUsage(900, 0, 4000, 64_000);
        var chat = new FakeChat().Answer(_ => throw new TMConcept.Core.Providers.ProviderException("cut")
        {
            Code = "truncated",
            ChargedUsage = charged,
            PartialText = "window.__timelines = ",
        });
        using var h = Harness(chat, engine);
        var (page, concept) = await PrepareAsync(h);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        var model = h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;
        var paid = h.Get<TMConcept.Core.Costs.ICostCalculator>().Exact(model, charged);

        await page.ProduceAllAsync();

        chat.Requests.Single().MaxOutputTokens.ShouldBe(VideoCodeBudget.MaxOutputTokens);
        chat.Requests.Single().Stream.ShouldBeTrue();
        var error = page.Queue.Single().Error;
        error.ShouldBe(text.Format("video.error.truncated", text.Usd(paid)));
        error.ShouldNotContain(text["provider.fail.truncated"]);
        page.LastCost.ShouldContain(text.Usd(paid));
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
    }

    [AvaloniaFact]
    public async Task ANarrationModelThatCannotBeResolvedGivesASilentVideoNotACrash()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (_, concept) = await PrepareAsync(h);

        // Kokoro speaks English but is not installed in the test folder: resolving the voice fails before anything is spoken.
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local,
            Model = "kokoro" }, CancellationToken.None);

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = true });

        record.Warnings.ShouldContain(VideoWarnings.SpeechFailed);
        record.Warnings.ShouldNotContain(VideoWarnings.SpeechOff);
        record.SpeechModel.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task SampleImagesChosenWithoutAnySampleSayWhyTheVideoHasNoImages()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (_, concept) = await PrepareAsync(h);

        var record = await RunAsync(h, concept, new VideoOptions { Engine = engine.Kind, Speech = false, Images = ImageSources.Samples });

        record.Warnings.ShouldContain(VideoWarnings.SamplesNone);
        engine.Input!.Scenes.ShouldAllBe(s => s.Image.Length == 0);
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        text["produced.note." + VideoWarnings.SamplesNone].ShouldNotStartWith("produced.note.");
    }

    private static async Task<VideoRecord> RunAsync(DesktopHarness h, Concept concept, VideoOptions options)
    {
        var profile = (await h.Get<IProfileStore>().GetAsync(concept.ProfileId, CancellationToken.None))!;
        var model = new ResolvedModel(h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!,
            new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-video-0123" }, string.Empty);
        var stored = (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!;
        return await h.Get<VideoPipeline>().RunAsync(new VideoJob(stored, profile, options, model), null, CancellationToken.None);
    }

    [AvaloniaFact]
    public async Task AnEngineThatIsNotInstalledIsOfferedAndDecliningStopsTheItemBeforeAnythingIsPaid()
    {
        var engine = new FakeEngine { Missing = ["packages", "browser"] };
        var chat = new FakeChat();
        var confirm = new FakeConfirm { Answer = false };
        var node = new FakeComponent(ComponentCodes.Node, ready: true);
        var ffmpeg = new FakeComponent(ComponentCodes.Ffmpeg, ready: false);
        using var h = Harness(chat, engine, s =>
        {
            s.AddSingleton<TMConcept.Desktop.Dialogs.IConfirmDialog>(confirm);
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(node, ffmpeg, new TMConcept.Engines.Components.EngineComponent(engine)));
        });
        var (page, concept) = await PrepareAsync(h);
        page.Picker.Row(PipelineSteps.VideoCode)!.Note.ShouldContain(h.Get<TMConcept.Desktop.Localization.Localizer>()["models.installs_on_start"]);

        await page.ProduceAllAsync();

        confirm.Messages.Single().ShouldContain("Chrome Headless Shell");
        confirm.Messages.Single().ShouldContain(ffmpeg.Name);
        ffmpeg.Installs.ShouldBe(0);
        chat.Requests.ShouldBeEmpty();
        page.Queue.Single().Error.ShouldContain(h.Get<TMConcept.Desktop.Localization.Localizer>()["video.error.missing"]);
        (await h.Get<IConceptStore>().GetAsync(concept.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Queued);
    }

    [AvaloniaFact]
    public async Task ItemsWithImagesAndNarrationOffAskForNoLocalImageOrVoiceModel()
    {
        var engine = new FakeEngine();
        var confirm = new FakeConfirm { Answer = false };
        var imageRuntime = new FakeComponent(ComponentCodes.ImageRuntime, ready: false);
        var weights = new FakeComponent(ComponentCodes.ImagePrefix + "z-image-turbo", ready: false, ComponentCodes.ImageRuntime);
        var speechRuntime = new FakeComponent(ComponentCodes.SpeechRuntime, ready: false);
        var voice = new FakeComponent(ComponentCodes.VoicePrefix + "supertonic", ready: false, ComponentCodes.SpeechRuntime);
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine, s =>
        {
            s.AddSingleton<TMConcept.Desktop.Dialogs.IConfirmDialog>(confirm);
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(imageRuntime, weights, speechRuntime, voice,
                new TMConcept.Engines.Components.EngineComponent(engine)));
        });
        // The profile's last choices are local models that are not installed; on the page images and narration are turned off.
        var (page, concept) = await PrepareAsync(h);
        var store = h.Get<IStepModelStore>();
        await store.SaveAsync(new StepModelChoice { Step = PipelineSteps.Images, ProfileId = concept.ProfileId, Provider = ModelProviders.Local, Model = "z-image-turbo" },
            CancellationToken.None);
        await store.SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local, Model = "supertonic" },
            CancellationToken.None);
        await page.ActivateAsync();
        page.Picker.Row(PipelineSteps.Images)!.Selected!.Model!.Id.ShouldBe("z-image-turbo", "the profile's last choice is shown");
        page.Picker.Row(PipelineSteps.Images)!.Selected = page.Picker.Row(PipelineSteps.Images)!.Options[0];
        page.Picker.Row(PipelineSteps.Speech)!.Selected = page.Picker.Row(PipelineSteps.Speech)!.Options[0];

        await page.ProduceAllAsync();

        confirm.Asked.ShouldBeEmpty();
        StepModelResolver.IsOff(await store.GetAsync(PipelineSteps.Images, concept.ProfileId, CancellationToken.None)).ShouldBeTrue("the new choice is kept");
        (weights.Installs + imageRuntime.Installs + voice.Installs + speechRuntime.Installs).ShouldBe(0);
        page.Queue.ShouldBeEmpty();
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().ImageModel.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task AVoiceHiddenAfterTheLanguageChangedIsTurnedOffAndNeverUsed()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);

        // The profile spoke English with Kokoro; then its content language became Turkish, which Kokoro does not speak.
        var store = h.Get<IStepModelStore>();
        await store.SaveAsync(new StepModelChoice { Step = PipelineSteps.Speech, ProfileId = concept.ProfileId, Provider = ModelProviders.Local, Model = "kokoro" },
            CancellationToken.None);
        var profile = (await h.Get<IProfileStore>().GetAsync(concept.ProfileId, CancellationToken.None))!;
        profile.ContentLanguage = ContentLanguages.Turkish;
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await page.ActivateAsync();
        page.Picker.Model(PipelineSteps.Speech).ShouldBeNull("the page shows narration off");

        await page.ProduceAllAsync();

        StepModelResolver.IsOff(await store.GetAsync(PipelineSteps.Speech, concept.ProfileId, CancellationToken.None)).ShouldBeTrue("what the page showed is saved");
        var video = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single();
        video.Warnings.ShouldContain(VideoWarnings.SpeechOff);
        video.Warnings.ShouldNotContain(VideoWarnings.SpeechFailed);
    }

    [AvaloniaFact]
    public async Task AnItemRemovedDuringTheRunIsNotMadeAndANewOneIs()
    {
        var engine = new FakeEngine();
        var chat = new FakeChat();
        using var h = Harness(chat, engine);
        var (page, first) = await PrepareAsync(h);
        var second = new Concept { ProfileId = first.ProfileId, Title = "Iconoclasm", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        var third = new Concept { ProfileId = first.ProfileId, Title = "Nika riots", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([second, third], CancellationToken.None);
        await page.ActivateAsync();
        await page.EnqueueAsync(page.Approved.Single(c => c.Id == second.Id));
        var queue = h.Get<IQueueStore>();
        chat.Answer(_ =>
        {
            // While the first video is being made the user removes the second item (✕) and queues the third.
            var items = queue.ListAsync(first.ProfileId, CancellationToken.None).GetAwaiter().GetResult();
            queue.RemoveAsync(items.Single(i => i.ConceptId == second.Id).Id, CancellationToken.None).GetAwaiter().GetResult();
            h.Get<IConceptStore>().SetStatusAsync([second.Id], ConceptStatus.Approved, CancellationToken.None).GetAwaiter().GetResult();
            queue.AddAsync(new QueueItem { ProfileId = first.ProfileId, ConceptId = third.Id, Position = 9, SettingsJson = items[0].SettingsJson },
                CancellationToken.None).GetAwaiter().GetResult();
            h.Get<IConceptStore>().SetStatusAsync([third.Id], ConceptStatus.Queued, CancellationToken.None).GetAwaiter().GetResult();
            return Code(engine.SampleCode);
        }).Answer(_ => Code(engine.SampleCode));

        await page.ProduceAllAsync();

        var made = await h.Get<IVideoStore>().ListAsync(first.ProfileId, CancellationToken.None);
        made.Select(v => v.Title).ShouldBe(["Greek fire", "Nika riots"], ignoreOrder: true);
        (await h.Get<IConceptStore>().GetAsync(second.Id, CancellationToken.None))!.Status.ShouldBe(ConceptStatus.Approved);
        chat.Requests.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task TheCodeModelChosenOnThePageIsUsedAndKeptForTheProfile()
    {
        var engine = new FakeEngine();
        using var h = Harness(new FakeChat().Answer(_ => Code(engine.SampleCode)), engine);
        var (page, concept) = await PrepareAsync(h);
        var code = page.Picker.Row(PipelineSteps.VideoCode)!;
        code.Selected!.Model!.Id.ShouldBe("claude-sonnet-5-5", "the profile's last choice is preselected");
        code.Selected = code.Options.First(o => o.Model?.Id == "claude-opus-5-5");
        page.Estimate.ShouldContain("$");

        await page.ProduceAllAsync();

        page.Queue.ShouldBeEmpty();
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().CodeModel.ShouldBe("anthropic/claude-opus-5-5");
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.VideoCode, concept.ProfileId, CancellationToken.None))!.Model.ShouldBe("claude-opus-5-5");
        page.LastCost.ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task TheImagesStepOffMakesNoImageCallAndAnImageModelGetsItsPromptsFromTheCodeModel()
    {
        var engine = new FakeEngine();
        var images = new FakeImages();
        var chat = new FakeChat().Answer(_ => Code(engine.SampleCode));
        using var h = Harness(chat, engine, s =>
        {
            s.RemoveAll<IImageEngine>();
            s.AddSingleton<IImageEngine>(images);
        });
        var (page, concept) = await PrepareAsync(h);
        page.Picker.Row(PipelineSteps.Images)!.Selected!.Kind.ShouldBe(StepOptionKind.None, "a profile starts with images off");

        await page.ProduceAllAsync();

        images.Prompts.ShouldBeEmpty();
        chat.Requests.Count.ShouldBe(1);
        (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single().ImageModel.ShouldBeEmpty();

        // With an image model: the code model writes the prompts first (the same model, one call), then each scene gets its image.
        var second = new Concept { ProfileId = concept.ProfileId, Title = "Iconoclasm", Summary = "s", Content = Content, Language = "en", Status = ConceptStatus.Approved };
        await h.Get<IConceptStore>().AddAsync([second], CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "fal", Label = "k", Secret = "fake-test-key-video-4567" }, CancellationToken.None);
        chat.Answer(_ => new ChatResponse("{\"prompts\":[\"a map\",\"burning ships\"]}", new ChatUsage(100, 0, 0, 50), null, "end_turn", "r"))
            .Answer(_ => Code(engine.SampleCode));
        await page.ActivateAsync();
        var row = page.Picker.Row(PipelineSteps.Images)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "fal-ai/flux-2/flash");
        await page.EnqueueAsync(page.Approved.Single());

        await page.ProduceAllAsync();

        images.Prompts.ShouldBe(["a map", "burning ships"]);
        chat.Requests.Count.ShouldBe(3);
        chat.Requests[1].System.ShouldBe(ImagePromptBuilder.System);
        var made = (await h.Get<IVideoStore>().ListAsync(concept.ProfileId, CancellationToken.None)).Single(v => v.Title == "Iconoclasm");
        made.ImageModel.ShouldBe("fal/fal-ai/flux-2/flash");
    }

    internal sealed class FakeImages : IImageEngine
    {
        public List<string> Prompts { get; } = [];

        public string Code => "fal";

        public Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken cancellationToken)
        {
            Prompts.Add(request.Prompt);
            Directory.CreateDirectory(Path.GetDirectoryName(request.Output)!);
            File.WriteAllBytes(request.Output, [137, 80, 78, 71]);
            return Task.FromResult(new ImageResult(request.Output, 0.01m));
        }
    }

    internal sealed class FakeEngine : IVideoEngine
    {
        public int FailRenders { get; set; }

        public TMConcept.Core.Render.RenderInput? Input { get; private set; }

        public EngineKind Kind => EngineKind.HyperFrames;

        public string Name => "Fake";

        public bool Experimental => false;

        public bool NeedsLicenseConsent => false;

        public string CodeFile => "scene.js";

        public string Contract => "contract";

        public string SampleCode => "window.__timelines = window.__timelines || {}; window.__timelines[\"concept\"] = gsap.timeline({ paused: true });";

        public IReadOnlyList<string> Missing { get; set; } = [];

        public EngineStatus Status() => new(Missing.Count == 0, Missing);

        public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public void WriteProject(EngineRenderRequest request)
        {
            var findings = EngineCodeCheck.Check(Kind, request.Code);
            if (findings.Count > 0)
            {
                throw new EngineException("rejected") { Code = "check", Findings = findings };
            }

            Input = request.Input;
            Directory.CreateDirectory(Path.Combine(request.Folder, "tmp"));
            File.WriteAllText(Path.Combine(request.Folder, CodeFile), request.Code);
            File.WriteAllText(Path.Combine(request.Folder, "input.json"), TMConcept.Core.Render.RenderInputBuilder.ToJson(request.Input));
            File.WriteAllText(Path.Combine(request.Folder, "tmp", "frame-cache.bin"), "x");
        }

        public Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken)
        {
            if (FailRenders-- > 0)
            {
                throw new EngineException("TypeError: x is undefined") { Code = "render" };
            }

            File.WriteAllBytes(request.Output, new byte[2048]);
            progress?.Report(new EngineEvent(1, "done", Done: true));
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeMedia : IMediaTools
    {
        public IReadOnlyList<string> RequiredComponents { get; } = [];

        public Task MuxAsync(string folder, string silentVideo, IReadOnlyList<(string Wav, long StartMs)> clips, string? assFile, string output,
            CancellationToken cancellationToken)
        {
            File.Copy(silentVideo, output, overwrite: true);
            return Task.CompletedTask;
        }

        public bool FailThumbnail { get; init; }

        public Task<bool> ThumbnailAsync(string video, string jpeg, CancellationToken cancellationToken) =>
            FailThumbnail ? throw new IOException("disk full") : Task.FromResult(false);
    }

    internal sealed class FakeVoice : TMConcept.Core.Speech.ISpeechEngine
    {
        public string Code => "kokoro";

        public string Name => "Fake voice";

        public bool IsLocal => true;

        public bool Ready => true;

        public bool Supports(string language) => language == ContentLanguages.English;

        public IReadOnlyList<TMConcept.Core.Speech.SpeechVoice> Voices(string language) => [new("v", "Voice")];

        public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TMConcept.Core.Speech.SpeechClip> SynthesizeAsync(TMConcept.Core.Speech.SpeechRequest request, CancellationToken cancellationToken)
        {
            TMConcept.Core.Speech.WaveFile.WritePcm16(request.OutputWav, new float[2400], 24_000);
            return Task.FromResult(new TMConcept.Core.Speech.SpeechClip(request.OutputWav, 100, request.Text.Length, 0, Code));
        }
    }
}

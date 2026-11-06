using System.Net;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

public sealed class KeysAndModelsTests
{
    [AvaloniaFact]
    public async Task AKeyIsSavedTestedAndShownMasked()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """{"data":[]}""");
        using var h = new DesktopHarness(s => s.AddSingleton(fake.Transport));
        await FirstRunTests.CreateAsync(h);
        var keys = h.Get<KeysSettingsViewModel>();
        await keys.ActivateAsync();
        keys.Provider = keys.Providers.First(p => p.Provider.Code == "anthropic");
        keys.Secret = "fake-test-key-0123456789abcdef";

        await keys.AddAsync();

        var row = keys.Keys.Single();
        row.Masked.ShouldNotContain("0123456789abcdef");
        row.Failed.ShouldBeFalse();
        fake.Requests.Single().Uri.Host.ShouldBe("api.anthropic.com");
        (await h.Get<IApiKeyStore>().ListAsync("anthropic", CancellationToken.None)).Single().LastOkAtUtc.ShouldNotBeNull();
        keys.Secret.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task NoStepHasAModelUntilTheUserChoosesOne()
    {
        using var h = new DesktopHarness(s => s.AddSingleton<IGpuInfo>(new FakeGpu(null)));
        await FirstRunTests.CreateAsync(h);
        var models = h.Get<ModelsSettingsViewModel>();

        await models.ActivateAsync();

        models.Rows.Count.ShouldBe(PipelineSteps.All.Count);
        models.Rows.ShouldAllBe(r => r.Selected!.Model == null);
        await models.SaveAsync();
        (await h.Get<IStepModelStore>().ListAsync(null, CancellationToken.None)).ShouldBeEmpty();

        var concepts = models.Rows.First(r => r.Step.Code == "concepts");
        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        concepts.Effort = concepts.Efforts.First(e => e.Value == "high");
        await models.SaveAsync();
        var saved = (await h.Get<IStepModelStore>().ListAsync(null, CancellationToken.None)).Single();
        saved.Model.ShouldBe("claude-sonnet-5-5");
        StepModelResolver.Options(saved).Effort.ShouldBe("high");
    }

    [AvaloniaFact]
    public async Task TheEffortPickerStartsWithAutomaticAndShowsOnlyForModelsThatTakeAnEffort()
    {
        using var h = new DesktopHarness(s => s.AddSingleton<IGpuInfo>(new FakeGpu(null)));
        await FirstRunTests.CreateAsync(h);
        var models = h.Get<ModelsSettingsViewModel>();
        await models.ActivateAsync();
        var text = h.Get<Localizer>();
        var concepts = models.Rows.First(r => r.Step.Code == PipelineSteps.Concepts);

        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");

        concepts.HasEfforts.ShouldBeTrue();
        concepts.Efforts[0].Value.ShouldBeEmpty();
        concepts.Efforts[0].Label.ShouldBe(text["models.effort.auto"]);
        concepts.Efforts.ShouldAllBe(e => e.Label.Length > 0);
        concepts.Effort.ShouldBe(concepts.Efforts[0]);

        concepts.Selected = concepts.Options.First(o => o.Model != null && o.Model.Provider == "zai" && o.Model.Efforts.Count == 0);
        concepts.HasEfforts.ShouldBeFalse();

        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        await models.SaveAsync();
        var saved = (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Concepts, null, CancellationToken.None))!;
        StepModelResolver.Options(saved).Effort.ShouldBeEmpty();

        // Shown again, the saved choice keeps "Automatic".
        await models.ActivateAsync();
        models.Rows.First(r => r.Step.Code == PipelineSteps.Concepts).Effort!.Value.ShouldBeEmpty();
    }

    private static DesktopHarness Installing(FakeConfirm confirm, params FakeComponent[] components) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(confirm);
        s.AddSingleton<IGpuInfo>(new FakeGpu(8L << 30));
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(components));
    });

    [AvaloniaFact]
    public async Task SavingInstallsOnlyWhatIsMissingAndSavesNothingForAStepWhoseInstallWasDeclinedOrFailed()
    {
        var runtime = new FakeComponent(ComponentCodes.SpeechRuntime, ready: false);
        var voice = new FakeComponent(ComponentCodes.VoicePrefix + "supertonic", ready: false, ComponentCodes.SpeechRuntime);
        var image = new FakeComponent(ComponentCodes.ImagePrefix + "flux2-klein-4b", ready: true, ComponentCodes.ImageRuntime);
        var confirm = new FakeConfirm();
        using var h = Installing(confirm, runtime, voice, image);
        await FirstRunTests.CreateAsync(h);
        var store = h.Get<IStepModelStore>();
        var models = h.Get<ModelsSettingsViewModel>();
        await models.ActivateAsync();
        var speech = models.Rows.First(r => r.Step.Code == PipelineSteps.Speech);
        var images = models.Rows.First(r => r.Step.Code == PipelineSteps.Images);
        speech.Selected = speech.Options.First(o => o.Model?.Id == "supertonic");
        images.Selected = images.Options.First(o => o.Model?.Id == "flux2-klein-4b");

        // The voice's terms are accepted, the download list is declined: the voice is not saved, the image model (ready) is.
        confirm.Answers.Enqueue(true);
        confirm.Answers.Enqueue(false);
        await models.SaveAsync();
        (await store.GetAsync(PipelineSteps.Speech, null, CancellationToken.None)).ShouldBeNull();
        (await store.GetAsync(PipelineSteps.Images, null, CancellationToken.None))!.Model.ShouldBe("flux2-klein-4b");
        (runtime.Installs + voice.Installs + image.Installs).ShouldBe(0);
        confirm.Messages[^1].ShouldContain(voice.Name);
        confirm.Messages[^1].ShouldContain(runtime.Name);
        confirm.Messages[^1].ShouldNotContain(image.Name);
        models.Status.ShouldContain(speech.Title);

        // Accepted, but the voice fails: nothing is saved for the step.
        voice.Fail = true;
        await models.SaveAsync();
        (await store.GetAsync(PipelineSteps.Speech, null, CancellationToken.None)).ShouldBeNull();
        runtime.Installs.ShouldBe(1);
        speech.NoteIsWarning.ShouldBeTrue();

        // Accepted and installed: saved; what is ready is not installed again.
        voice.Fail = false;
        await models.SaveAsync();
        (await store.GetAsync(PipelineSteps.Speech, null, CancellationToken.None))!.Model.ShouldBe("supertonic");
        runtime.Installs.ShouldBe(1);
        voice.Installs.ShouldBe(2);
        image.Installs.ShouldBe(0);
        models.Status.ShouldBe(h.Get<Localizer>()["common.saved"]);
    }

    [AvaloniaFact]
    public async Task AProfileOverridesAStepAndOtherProfilesKeepTheGeneralSetting()
    {
        using var h = new DesktopHarness(s => s.AddSingleton<IGpuInfo>(new FakeGpu(null)));
        await FirstRunTests.CreateAsync(h);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "k", Secret = "fake-test-key-models-0123" }, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "fal", Label = "k", Secret = "fake-test-key-models-4567" }, CancellationToken.None);
        var store = h.Get<IStepModelStore>();
        var resolver = h.Get<StepModelResolver>();
        var own = new Profile { Name = "Own", ConceptText = "x" };
        var plain = new Profile { Name = "Plain", ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(own, CancellationToken.None);
        await h.Get<IProfileStore>().SaveAsync(plain, CancellationToken.None);
        await store.SaveAsync(new StepModelChoice { Step = PipelineSteps.Concepts, Provider = "anthropic", Model = "claude-sonnet-5-5" }, CancellationToken.None);
        await store.SaveAsync(new StepModelChoice { Step = PipelineSteps.Images, Provider = "fal", Model = "fal-ai/flux-2/flash" }, CancellationToken.None);

        // The profile page lists "use the general setting" first, naming the general choice.
        var page = h.Get<ProfileViewModel>();
        await h.Get<AppSession>().SelectProfileAsync(own, CancellationToken.None);
        await page.ActivateAsync();
        var concepts = page.Steps.Rows.First(r => r.Step.Code == PipelineSteps.Concepts);
        concepts.Selected!.Kind.ShouldBe(StepOptionKind.Inherit);
        concepts.Selected.Label.ShouldContain("Claude Sonnet 5.5");
        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        var images = page.Steps.Rows.First(r => r.Step.Code == PipelineSteps.Images);
        images.Selected = images.Options.First(o => o.Kind == StepOptionKind.None);
        await page.SaveAsync();

        (await resolver.ResolveForAsync(PipelineSteps.Concepts, own.Id, CancellationToken.None)).Model.Id.ShouldBe("claude-haiku-5-5");
        (await resolver.ResolveForAsync(PipelineSteps.Concepts, plain.Id, CancellationToken.None)).Model.Id.ShouldBe("claude-sonnet-5-5");
        (await resolver.ResolveOptionalAsync(PipelineSteps.Images, own.Id, CancellationToken.None)).ShouldBeNull();
        (await resolver.ResolveOptionalAsync(PipelineSteps.Images, plain.Id, CancellationToken.None))!.Model.Id.ShouldBe("fal-ai/flux-2/flash");

        // Back to the general setting: the override is gone.
        concepts.Selected = concepts.Options.First(o => o.Kind == StepOptionKind.Inherit);
        await page.SaveAsync();
        (await store.GetAsync(PipelineSteps.Concepts, own.Id, CancellationToken.None)).ShouldBeNull();
        (await resolver.ResolveForAsync(PipelineSteps.Concepts, own.Id, CancellationToken.None)).Model.Id.ShouldBe("claude-sonnet-5-5");
    }

    [AvaloniaFact]
    public async Task TheToolsTabIsGone()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var settings = h.Get<SettingsViewModel>();

        await settings.ActivateAsync();

        settings.Sections.Select(s => s.TitleKey).ShouldNotContain("settings.tools");
        h.Get<Localizer>().Strings.Keys.ShouldNotContain("settings.tools");
        typeof(SettingsViewModel).Assembly.GetTypes().Select(t => t.Name).ShouldNotContain("ToolsSettingsViewModel");
    }
}

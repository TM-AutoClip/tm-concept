using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>Saving models from the profile page and Settings → Models, and the installed components list (fake components; nothing is downloaded).</summary>
public sealed class InstallOnSaveTests
{
    private static DesktopHarness Installing(FakeConfirm confirm, params FakeComponent[] components) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(confirm);
        s.AddSingleton<IGpuInfo>(new FakeGpu(8L << 30));
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(components));
    });

    [AvaloniaFact]
    public async Task SavingAProfileInTheAppKeepsItsModelOverrides()
    {
        // The real shell: saving selects the profile again, and that reloads every page (this one too). The models must be stored first.
        using var h = Installing(new FakeConfirm());
        await FirstRunTests.CreateAsync(h);
        var own = new Profile { Name = "Own", ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(own, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(own, CancellationToken.None);
        var shell = h.Get<ShellViewModel>();
        await shell.StartAsync();
        var page = (await shell.NavigateAsync<ProfileViewModel>()).ShouldNotBeNull();
        var concepts = page.Steps.Rows.First(r => r.Step.Code == PipelineSteps.Concepts);
        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-haiku-5-5");

        await page.SaveAsync();

        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Concepts, own.Id, CancellationToken.None))!.Model.ShouldBe("claude-haiku-5-5");
        page.Status.ShouldBe(h.Get<Localizer>()["common.saved"]);
        page.Steps.Rows.First(r => r.Step.Code == PipelineSteps.Concepts).Selected!.Model!.Id.ShouldBe("claude-haiku-5-5");
    }

    [AvaloniaFact]
    public async Task AnUnchangedStepWhoseComponentWasRemovedIsInstalledOnSave()
    {
        var runtime = new FakeComponent(ComponentCodes.SpeechRuntime, ready: true);
        var voice = new FakeComponent(ComponentCodes.VoicePrefix + "supertonic", ready: true, ComponentCodes.SpeechRuntime);
        var confirm = new FakeConfirm();
        using var h = Installing(confirm, runtime, voice);
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var models = h.Get<ModelsSettingsViewModel>();
        await models.ActivateAsync();
        var speech = models.Rows.First(r => r.Step.Code == PipelineSteps.Speech);
        speech.Selected = speech.Options.First(o => o.Model?.Id == "supertonic");
        await models.SaveAsync();
        voice.Installs.ShouldBe(0);

        // The voice was removed (Settings → General): the note says it is installed on save, and Save does install it.
        voice.Ready = false;
        await models.ActivateAsync();
        speech = models.Rows.First(r => r.Step.Code == PipelineSteps.Speech);
        speech.Note.ShouldContain(text["models.installs_on_save"]);

        await models.SaveAsync();

        voice.Installs.ShouldBe(1);
        confirm.Messages[^1].ShouldContain(voice.Name);
        speech.Note.ShouldNotContain(text["models.installs_on_save"]);
        models.Status.ShouldBe(text["common.saved"]);
    }

    [AvaloniaFact]
    public async Task AComponentWithoutADownloadHereDoesNotHoldBackAnUnrelatedStep()
    {
        var imageRuntime = new FakeComponent(ComponentCodes.ImageRuntime, ready: false) { Supported = false };
        var weights = new FakeComponent(ComponentCodes.ImagePrefix + "flux2-klein-4b", ready: false, ComponentCodes.ImageRuntime);
        var speechRuntime = new FakeComponent(ComponentCodes.SpeechRuntime, ready: false);
        var voice = new FakeComponent(ComponentCodes.VoicePrefix + "supertonic", ready: false, ComponentCodes.SpeechRuntime);
        var confirm = new FakeConfirm();
        using var h = Installing(confirm, imageRuntime, weights, speechRuntime, voice);
        await FirstRunTests.CreateAsync(h);
        var store = h.Get<IStepModelStore>();
        var models = h.Get<ModelsSettingsViewModel>();
        await models.ActivateAsync();
        var speech = models.Rows.First(r => r.Step.Code == PipelineSteps.Speech);
        var images = models.Rows.First(r => r.Step.Code == PipelineSteps.Images);
        speech.Selected = speech.Options.First(o => o.Model?.Id == "supertonic");
        images.Selected = images.Options.First(o => o.Model?.Id == "flux2-klein-4b");

        await models.SaveAsync();

        (await store.GetAsync(PipelineSteps.Speech, null, CancellationToken.None))!.Model.ShouldBe("supertonic");
        (await store.GetAsync(PipelineSteps.Images, null, CancellationToken.None)).ShouldBeNull();
        voice.Installs.ShouldBe(1);
        weights.Installs.ShouldBe(0, "its program has no download here: its weights are not fetched for nothing");
        confirm.Messages[^1].ShouldNotContain(weights.Name);
        images.Note.ShouldContain(imageRuntime.Name);
        models.Status.ShouldContain(images.Title);
    }

    [Fact]
    public async Task OneFailedComponentSkipsOnlyWhatDependsOnIt()
    {
        var broken = new FakeComponent("broken", ready: false) { Fail = true };
        var child = new FakeComponent("child", ready: false, "broken");
        var other = new FakeComponent("other", ready: false);
        var planner = new ComponentPlanner([new FakeComponents(broken, child, other)], NullLogger<ComponentPlanner>.Instance);
        var plan = await planner.PlanAsync(["child", "other"], CancellationToken.None);

        await Should.ThrowAsync<ToolInstallException>(() => planner.InstallAsync(plan, null, CancellationToken.None));

        other.Ready.ShouldBeTrue();
        child.Installs.ShouldBe(0);
        broken.Installs.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task CancellingAnInstallIsAnOutcomeNotACrash()
    {
        var engine = new FakeComponent(ComponentCodes.EnginePrefix + "hyperframes", ready: false) { Cancel = true };
        using var h = Installing(new FakeConfirm(), engine);
        await FirstRunTests.CreateAsync(h);

        var outcome = await h.Get<ComponentInstallFlow>().EnsureAsync([engine.Code], "install.purpose.video", null, CancellationToken.None);

        outcome.Ready.ShouldBeFalse();
        outcome.Reason.ShouldBe(h.Get<Localizer>()["install.cancelled"]);
    }

    [AvaloniaFact]
    public async Task TheInstalledListLooksAtTheSystemFirstAndShowsIncompleteComponents()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "component-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "part.gguf"), new byte[2_000_000]);
        try
        {
            // A program found on the PATH (only when looked at), and weights on disk whose program was removed.
            var node = new FakeComponent(ComponentCodes.Node, ready: false) { FoundOnRefresh = true };
            var weights = new FakeComponent(ComponentCodes.ImagePrefix + "z-image-turbo", ready: false, ComponentCodes.ImageRuntime) { Folder = folder };
            using var h = Installing(new FakeConfirm(), node, weights);
            await FirstRunTests.CreateAsync(h);
            var general = h.Get<GeneralSettingsViewModel>();

            await general.ActivateAsync();

            var system = general.Installed.Single(r => r.Component == node);
            system.Size.ShouldBe(h.Get<Localizer>()["components.system"]);
            system.Incomplete.ShouldBeFalse();
            var incomplete = general.Installed.Single(r => r.Component == weights);
            incomplete.Incomplete.ShouldBeTrue();
            incomplete.CanRemove.ShouldBeTrue();
            incomplete.Size.ShouldBe("2 MB");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

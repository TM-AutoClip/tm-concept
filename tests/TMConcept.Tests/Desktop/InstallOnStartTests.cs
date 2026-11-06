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

/// <summary>
/// What a chosen model needs is installed when a process starts (one confirmation for every download), and the installed components list
/// (fake components; nothing is downloaded).
/// </summary>
public sealed class InstallOnStartTests
{
    private static DesktopHarness Installing(FakeConfirm confirm, params FakeComponent[] components) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(confirm);
        s.AddSingleton<IGpuInfo>(new FakeGpu(8L << 30));
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(components));
    });

    private static async Task<(StepPickerViewModel Picker, Profile Profile)> VideoPickerAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var settings = h.Get<AppSession>().Settings;
        settings.SupertonicLicenseAccepted = true;
        await h.Get<AppSession>().SaveAsync(settings, CancellationToken.None);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        return (picker, profile);
    }

    [AvaloniaFact]
    public async Task StartingInstallsOnlyWhatIsMissingAndKeepsNothingWhenTheDownloadIsDeclinedOrFails()
    {
        var runtime = new FakeComponent(ComponentCodes.SpeechRuntime, ready: false);
        var voice = new FakeComponent(ComponentCodes.VoicePrefix + "supertonic", ready: false, ComponentCodes.SpeechRuntime);
        var image = new FakeComponent(ComponentCodes.ImagePrefix + "flux2-klein-4b", ready: true, ComponentCodes.ImageRuntime);
        var confirm = new FakeConfirm();
        using var h = Installing(confirm, runtime, voice, image);
        var (picker, profile) = await VideoPickerAsync(h);
        var store = h.Get<IStepModelStore>();
        var text = h.Get<Localizer>();
        var speech = picker.Row(PipelineSteps.Speech)!;
        var images = picker.Row(PipelineSteps.Images)!;
        speech.Selected = speech.Options.First(o => o.Model?.Id == "supertonic");
        images.Selected = images.Options.First(o => o.Model?.Id == "flux2-klein-4b");
        speech.Note.ShouldContain(text["models.installs_on_start"]);
        string[] needed = [PipelineSteps.Speech, PipelineSteps.Images];

        // The download list is declined: nothing starts and nothing is kept.
        confirm.Answers.Enqueue(false);
        (await picker.PrepareAsync(needed, "install.purpose.video", CancellationToken.None)).ShouldBeFalse();
        (await store.GetAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None)).ShouldBeNull();
        (runtime.Installs + voice.Installs + image.Installs).ShouldBe(0);
        confirm.Messages[^1].ShouldContain(voice.Name);
        confirm.Messages[^1].ShouldContain(runtime.Name);
        confirm.Messages[^1].ShouldNotContain(image.Name);
        picker.Status.ShouldNotBeEmpty();

        // Accepted, but the voice fails: nothing starts.
        voice.Fail = true;
        (await picker.PrepareAsync(needed, "install.purpose.video", CancellationToken.None)).ShouldBeFalse();
        runtime.Installs.ShouldBe(1);
        (await store.GetAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None)).ShouldBeNull();

        // Accepted and installed: the choices are kept for the profile; what is ready is not installed again.
        voice.Fail = false;
        (await picker.PrepareAsync(needed, "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        (await store.GetAsync(PipelineSteps.Speech, profile.Id, CancellationToken.None))!.Model.ShouldBe("supertonic");
        (await store.GetAsync(PipelineSteps.Images, profile.Id, CancellationToken.None))!.Model.ShouldBe("flux2-klein-4b");
        runtime.Installs.ShouldBe(1);
        voice.Installs.ShouldBe(2);
        image.Installs.ShouldBe(0);
        speech.Note.ShouldNotContain(text["models.installs_on_start"]);
    }

    [AvaloniaFact]
    public async Task AKeptChoiceWhoseComponentWasRemovedIsInstalledAgainAtTheNextStart()
    {
        var runtime = new FakeComponent(ComponentCodes.SpeechRuntime, ready: true);
        var voice = new FakeComponent(ComponentCodes.VoicePrefix + "supertonic", ready: true, ComponentCodes.SpeechRuntime);
        var confirm = new FakeConfirm();
        using var h = Installing(confirm, runtime, voice);
        var (picker, profile) = await VideoPickerAsync(h);
        var speech = picker.Row(PipelineSteps.Speech)!;
        speech.Selected = speech.Options.First(o => o.Model?.Id == "supertonic");
        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        voice.Installs.ShouldBe(0);

        // The voice was removed (Settings → General): shown again, the note says it is installed at the start, and the start installs it.
        voice.Ready = false;
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        speech = picker.Row(PipelineSteps.Speech)!;
        speech.Selected!.Model!.Id.ShouldBe("supertonic");
        speech.Note.ShouldContain(h.Get<Localizer>()["models.installs_on_start"]);

        (await picker.PrepareAsync([PipelineSteps.Speech], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();

        voice.Installs.ShouldBe(1);
        confirm.Messages[^1].ShouldContain(voice.Name);
    }

    [AvaloniaFact]
    public async Task AComponentWithoutADownloadHereStopsTheStartWithoutFetchingItsWeights()
    {
        var imageRuntime = new FakeComponent(ComponentCodes.ImageRuntime, ready: false) { Supported = false };
        var weights = new FakeComponent(ComponentCodes.ImagePrefix + "flux2-klein-4b", ready: false, ComponentCodes.ImageRuntime);
        var confirm = new FakeConfirm();
        using var h = Installing(confirm, imageRuntime, weights);
        var (picker, profile) = await VideoPickerAsync(h);
        var images = picker.Row(PipelineSteps.Images)!;
        images.Selected = images.Options.First(o => o.Model?.Id == "flux2-klein-4b");

        (await picker.PrepareAsync([PipelineSteps.Images], "install.purpose.video", CancellationToken.None)).ShouldBeFalse();

        weights.Installs.ShouldBe(0, "its program has no download here: its weights are not fetched for nothing");
        picker.Status.ShouldContain(imageRuntime.Name);
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Images, profile.Id, CancellationToken.None)).ShouldBeNull();
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

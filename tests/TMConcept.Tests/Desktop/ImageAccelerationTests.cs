using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The graphics card in the interface: the memory note of a local image model (red when the card is too small, green when it fits,
/// yellow on the processor or with unknown memory) and the Image acceleration setting (per computer, in preferences.json; a change
/// installs the new build when a local image model is on disk). Fake card reports and fake components: nothing native, nothing downloaded.
/// </summary>
public sealed class ImageAccelerationTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static DesktopHarness Harness(GpuReport report, FakeConfirm? confirm = null, params FakeComponent[] components) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(confirm ?? new FakeConfirm());
        s.AddSingleton<IGpuInfo>(new FakeGpu(report));
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(components));
    });

    private static async Task<StepModelRowViewModel> ImagesRowAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        return picker.Row(PipelineSteps.Images)!;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
    }

    private static void Choose(StepModelRowViewModel row, string model) => row.Selected = row.Options.First(o => o.Model?.Id == model);

    [AvaloniaFact]
    public async Task ATooSmallCardIsRedButStaysSelectableAndAFittingOneIsGreen()
    {
        using var h = Harness(new GpuReport([new GpuAdapter(GpuVendor.Amd, "AMD Radeon RX 7600", 8 * GiB)], false, true));
        var images = await ImagesRowAsync(h);
        var text = h.Get<Localizer>();

        Choose(images, "z-image-turbo-full");
        await Until(() => images.NoteIsError);

        images.Selected!.Model!.Id.ShouldBe("z-image-turbo-full", "a red note never blocks the choice");
        images.Note.ShouldContain(text.Format("images.memory.not_enough", 24, "8"));
        images.Note.ShouldNotContain("CUDA on Windows");
        images.NoteIsWarning.ShouldBeFalse();
        images.NoteIsOk.ShouldBeFalse();

        Choose(images, "flux2-klein-4b");
        await Until(() => images.NoteIsOk);

        images.Note.ShouldContain(text.Format("images.memory.ok", "8", "AMD Radeon RX 7600", text["settings.image_acceleration.vulkan"]));
        images.NoteIsError.ShouldBeFalse();
        images.NoteIsWarning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task EachLocalImageModelShowsItsClassInTheCardsColourAndAHigherOneIsNeverDownloaded()
    {
        var weights = new FakeComponent(ComponentCodes.ImagePrefix + "qwen-image-q8", ready: false, ComponentCodes.ImageRuntime);
        var confirm = new FakeConfirm();
        // A "16 GB" card that reports 15.8 GiB is of the 16 GB class.
        using var h = Harness(new GpuReport([new GpuAdapter(GpuVendor.Amd, "AMD Radeon RX 7800 XT", (long)(15.8 * GiB))], false, true), confirm, weights);
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        var images = picker.Row(PipelineSteps.Images)!;
        var text = h.Get<Localizer>();
        StepOption Option(string id) => images.Options.Single(o => o.Model?.Id == id);

        Option("flux2-klein-4b").IsOk.ShouldBeTrue();
        Option("flux2-klein-4b").Label.ShouldContain(text.Format("models.tier", 4));
        Option("qwen-image").IsOk.ShouldBeTrue("its own class");
        Option("qwen-image-q8").IsError.ShouldBeTrue();
        Option("qwen-image-q8").Label.ShouldContain(text["models.tier.too_big"]);
        Option("z-image-turbo-full").IsError.ShouldBeTrue("24 GB is above this card");
        // 0.1.18: a non-commercial license is orange, never red (red only means "this card cannot run it").
        Option("flux2-klein-9b").IsError.ShouldBeFalse("red is only for a model this card cannot run");
        Option("flux2-klein-9b").IsCaution.ShouldBeTrue("non-commercial");
        Option("flux2-klein-9b").Label.ShouldContain(text["models.license.badge.noncommercial"]);
        Option("sd35-medium").IsWarning.ShouldBeTrue("a license with conditions");
        Option("sd35-medium").Label.ShouldContain(text["models.license.badge.limited"]);

        // A model above the card's class stays chosen, but a process never downloads it.
        Choose(images, "qwen-image-q8");
        await Until(() => images.NoteIsError);
        (await picker.PrepareAsync([PipelineSteps.Images], "install.purpose.video", CancellationToken.None)).ShouldBeFalse();
        picker.Status.ShouldBe(text.Format("picker.not_started.memory", "Qwen-Image (Q8)", 32, "15.8"));
        images.Selected!.Model!.Id.ShouldBe("qwen-image-q8", "the choice is kept");
        weights.Installs.ShouldBe(0);
        confirm.Asked.ShouldBeEmpty("nothing is offered for download");

        // A non-commercial model above the card's class: refused before its licence is asked, so no acceptance is kept for it.
        Choose(images, "flux2-klein-9b-q8");
        await Until(() => images.NoteIsError);
        (await picker.PrepareAsync([PipelineSteps.Images], "install.purpose.video", CancellationToken.None)).ShouldBeFalse();
        picker.Status.ShouldBe(text.Format("picker.not_started.memory", "FLUX.2 klein 9B (Q8)", 24, "15.8"));
        confirm.Asked.ShouldBeEmpty("no licence dialog for a model this card cannot run");
        h.Get<TMConcept.Desktop.Startup.AppSession>().Settings.AcceptedModelLicenses.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ANonCommercialModelIsAskedOnceBeforeItsDownloadAndADeclineStartsNothing()
    {
        var weights = new FakeComponent(ComponentCodes.ImagePrefix + "flux2-klein-9b", ready: false, ComponentCodes.ImageRuntime);
        var runtime = new FakeComponent(ComponentCodes.ImageRuntime, ready: true);
        var confirm = new FakeConfirm();
        using var h = Harness(new GpuReport([new GpuAdapter(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GiB)], false, true), confirm, weights, runtime);
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        var images = picker.Row(PipelineSteps.Images)!;
        var text = h.Get<Localizer>();
        Choose(images, "flux2-klein-9b");
        await Until(() => images.Note.Contains(text.Format("models.license.note.noncommercial", text["license.flux-non-commercial"]), StringComparison.Ordinal));

        // Declined: nothing is downloaded.
        confirm.Answers.Enqueue(false);
        (await picker.PrepareAsync([PipelineSteps.Images], "install.purpose.video", CancellationToken.None)).ShouldBeFalse();
        confirm.Asked.ShouldBe([text.Format("models.license.title", "FLUX.2 klein 9B")]);
        confirm.Dangers.ShouldBe([false], "0.1.18: a license question is not a danger question (red is only for a model this card cannot run)");
        images.NoteIsCaution.ShouldBeTrue("non-commercial: orange");
        images.NoteIsError.ShouldBeFalse();
        confirm.Messages[0].ShouldContain(text["license.flux-non-commercial.terms"]);
        confirm.Messages[0].ShouldContain("https://huggingface.co/black-forest-labs/FLUX.2-klein-9B");
        picker.Status.ShouldBe(text.Format("picker.not_started.license", images.Title));
        weights.Installs.ShouldBe(0);

        // Accepted: kept and never asked again; the download list is confirmed and the weights install.
        (await picker.PrepareAsync([PipelineSteps.Images], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        weights.Installs.ShouldBe(1);
        h.Get<TMConcept.Desktop.Startup.AppSession>().Settings.AcceptedModelLicenses.ShouldBe(["local/flux2-klein-9b"]);
        (await picker.PrepareAsync([PipelineSteps.Images], "install.purpose.video", CancellationToken.None)).ShouldBeTrue();
        confirm.Asked.Count(t => t == text.Format("models.license.title", "FLUX.2 klein 9B")).ShouldBe(2, "asked at the decline and once more; never after it was accepted");
    }

    [AvaloniaFact]
    public async Task WithoutACardTheProcessorIsNamedAndUnknownMemoryIsAWarning()
    {
        using (var h = Harness(GpuReport.None))
        {
            var images = await ImagesRowAsync(h);
            Choose(images, "flux2-klein-4b");
            await Until(() => images.Note.Contains(h.Get<Localizer>()["images.memory.cpu"], StringComparison.Ordinal));
            images.NoteIsWarning.ShouldBeTrue();
            images.NoteIsError.ShouldBeFalse();
        }

        using (var h = Harness(new GpuReport([new GpuAdapter(GpuVendor.Intel, "Intel Arc A770", null)], false, true)))
        {
            var images = await ImagesRowAsync(h);
            Choose(images, "flux2-klein-4b");
            await Until(() => images.Note.Contains(h.Get<Localizer>()["images.memory.unknown"], StringComparison.Ordinal));
            images.NoteIsWarning.ShouldBeTrue();
            images.NoteIsOk.ShouldBeFalse();
        }
    }

    [AvaloniaFact]
    public async Task TheSettingShowsTheAutomaticChoiceAndIsKeptForThisComputer()
    {
        var confirm = new FakeConfirm();
        using var h = Harness(new GpuReport([new GpuAdapter(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GiB)], false, true), confirm);
        await FirstRunTests.CreateAsync(h);
        var general = h.Get<GeneralSettingsViewModel>();
        var text = h.Get<Localizer>();

        await general.ActivateAsync();

        general.AccelerationOptions.Select(o => o.Value).ShouldBe(["Automatic", "Cuda", "Vulkan", "Cpu"]);
        general.Acceleration!.Value.ShouldBe("Automatic");
        general.AccelerationNote.ShouldBe(text.Format("settings.image_acceleration.detected", "AMD Radeon RX 7800 XT · 16 GB",
            text["settings.image_acceleration.vulkan"]));

        general.Acceleration = general.AccelerationOptions.Single(o => o.Value == "Cpu");
        await general.AccelerationApplied;

        h.Get<UiPreferencesFile>().Read().ImageAcceleration.ShouldBe(ImageAcceleration.Cpu);
        h.Get<IImageAccelerationSetting>().Value.ShouldBe(ImageAcceleration.Cpu);
        h.Get<TMConcept.Engines.Images.ImageBackendSelector>().Choose().Backend.ShouldBe(ImageBackend.Cpu);
        general.Status.ShouldBe(text.Format("settings.image_acceleration.saved", text["settings.image_acceleration.cpu"]));
        confirm.Asked.ShouldBeEmpty("no local image model is on disk: the build comes with the first one");

        // Shown again: the stored choice is selected.
        await general.ActivateAsync();
        general.Acceleration!.Value.ShouldBe("Cpu");
    }

    [AvaloniaFact]
    public async Task AChangeInstallsTheNewBuildWhenALocalImageModelIsOnDisk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "part.gguf"), new byte[1000]);
        try
        {
            var confirm = new FakeConfirm { Answer = false };
            var runtime = new FakeComponent(ComponentCodes.ImageRuntime, ready: false);
            var weights = new FakeComponent(ComponentCodes.ImagePrefix + "flux2-klein-4b", ready: false, ComponentCodes.ImageRuntime) { Folder = folder };
            using var h = Harness(new GpuReport([new GpuAdapter(GpuVendor.Amd, "AMD Radeon RX 7800 XT", 16 * GiB)], false, true), confirm, runtime, weights);
            await FirstRunTests.CreateAsync(h);
            var general = h.Get<GeneralSettingsViewModel>();
            await general.ActivateAsync();

            general.Acceleration = general.AccelerationOptions.Single(o => o.Value == "Vulkan");
            await general.AccelerationApplied;

            confirm.Asked.Count.ShouldBe(1, "one confirmation lists the new build");
            general.Status.ShouldBe(h.Get<Localizer>()["install.declined"]);
            runtime.Installs.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

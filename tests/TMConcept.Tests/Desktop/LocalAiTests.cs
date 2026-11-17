using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Components;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.DiskSpace;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Core;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Settings → Local AI, the pickers and the storage manager with the app's own Ollama copy (0.1.20): the card class picks the recommended
/// models, nothing is pulled or started before the user confirms, an installed model is offered in every text step without a key, and the
/// storage manager lists it with who uses it and removes it through the recycle bin. Fake launcher, API and bin: no process, no network,
/// and the user's own Ollama is never touched.
/// </summary>
public sealed class LocalAiTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static DesktopHarness Harness(FakeConfirm confirm, FakeAppOllamaLauncher launcher, FakeAppOllamaApi api, FakeBin? bin = null) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(confirm);
        s.AddSingleton<IGpuInfo>(new FakeGpu(6L << 30));
        s.AddSingleton<IAppOllamaLauncher>(launcher);
        s.AddSingleton<IAppOllamaApi>(api);
        if (bin is not null)
        {
            s.RemoveAll<IRecycleBin>();
            s.AddSingleton<IRecycleBin>(bin);
        }
    });

    /// <summary>Puts a finished copy of the app's Ollama program in place (as if it had been installed earlier).</summary>
    private static void InstallProgram(DesktopHarness h)
    {
        var runtime = h.Get<TMConcept.Engines.AppOllama.AppOllamaComponent>();
        Directory.CreateDirectory(Path.GetDirectoryName(runtime.Executable)!);
        File.WriteAllText(runtime.Executable, "fake program");
        File.WriteAllText(Path.Combine(runtime.InstallFolder, ".installed"), "{}");
        runtime.Ready.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task TheCardClassComesFirstAndNothingIsDownloadedWithoutConfirmation()
    {
        var confirm = new FakeConfirm { Answer = false };
        var launcher = new FakeAppOllamaLauncher();
        var api = new FakeAppOllamaApi();
        using var h = Harness(confirm, launcher, api);
        await FirstRunTests.CreateAsync(h);
        var section = h.Get<LocalAiSettingsViewModel>();

        await section.ActivateAsync();

        section.RuntimeReady.ShouldBeFalse();
        section.CanInstallRuntime.ShouldBeTrue();
        section.CardLine.ShouldContain("6");
        var yours = section.Groups[0];
        yours.IsYourCard.ShouldBeTrue();
        yours.Rows.ShouldContain(r => r.Tag == "gemma4:e4b-it-qat", "a 6 GB card is offered a Gemma 4 model of the 4B class");
        yours.Rows.ShouldContain(r => r.Fits);
        section.Groups.SelectMany(g => g.Rows).ShouldContain(r => r.DoesNotFit, "larger classes are shown red");

        // Download is asked first; declined, nothing is pulled, no server starts and nothing appears in the pickers.
        var row = yours.Rows.First(r => r.Fits);
        await section.DownloadCommand.ExecuteAsync(row);
        confirm.Messages.ShouldHaveSingleItem().ShouldContain(row.Tag);
        confirm.Messages[0].ShouldContain(AppOllamaDefaults.Name, customMessage: "the program comes first in the same list");
        api.Pulls.ShouldBeEmpty();
        launcher.Started.ShouldBeEmpty();
        h.Get<IModelCatalog>().All.ShouldNotContain(m => m.Provider == ModelProviders.AppOllama);
        row.Installed.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task AConfirmedModelIsPulledThroughTheAppsServerAndOfferedInEveryTextStep()
    {
        var confirm = new FakeConfirm { Answer = true };
        var launcher = new FakeAppOllamaLauncher();
        var api = new FakeAppOllamaApi();
        using var h = Harness(confirm, launcher, api);
        await FirstRunTests.CreateAsync(h);
        InstallProgram(h);
        var store = h.Get<AppModelStore>();
        api.OnPull = tag => AppModelFiles.Write(store, tag, "weights of " + tag);
        var section = h.Get<LocalAiSettingsViewModel>();
        await section.ActivateAsync();
        var row = section.Groups[0].Rows.First(r => r.Fits);

        await section.DownloadCommand.ExecuteAsync(row);

        api.Pulls.ShouldBe([row.Tag]);
        var launch = launcher.Started.ShouldHaveSingleItem().Launch;
        launch.Port.ShouldNotBe(AppOllamaDefaults.UserOllamaPort);
        launch.Environment["OLLAMA_MODELS"].ShouldBe(Path.Combine(h.Paths.ToolsDirectory, "ollama-models"));
        section.Groups[0].Rows.First(r => r.Tag == row.Tag).Installed.ShouldBeTrue();

        // Offered in the text steps of the concept page and the video page, free and without a key.
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var option = picker.Row(PipelineSteps.Topics)!.Options.Where(o => o.Model?.Provider == ModelProviders.AppOllama).ShouldHaveSingleItem();
        option.Model!.Id.ShouldBe(row.Tag);
        picker.Row(PipelineSteps.Concepts)!.Options.ShouldContain(o => o.Model != null && o.Model.Id == row.Tag);
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        picker.Row(PipelineSteps.VideoCode)!.Options.ShouldContain(o => o.Model != null && o.Model.Id == row.Tag);
        (await h.Get<StepModelResolver>().ProvidersWithoutKeyAsync([option.Model], None)).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task TheStorageManagerListsAnAppModelWithItsUsersAndRemovesItThroughTheBin()
    {
        var bin = new FakeBin();
        var launcher = new FakeAppOllamaLauncher();
        using var h = Harness(new FakeConfirm { Answer = true }, launcher, new FakeAppOllamaApi(), bin);
        await FirstRunTests.CreateAsync(h);
        InstallProgram(h);
        var store = h.Get<AppModelStore>();
        AppModelFiles.Write(store, "gemma4:e4b-it-qat", "weights e4b", "shared");
        var shared = AppModelFiles.Write(store, "qwen3.5:4b-q4_K_M", "weights qwen", "shared");
        h.Get<AppModelRegistry>().Refresh();
        var profile = new Profile { Name = "Ottoman", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice
        {
            Step = PipelineSteps.Concepts, ProfileId = profile.Id, Provider = ModelProviders.AppOllama, Model = "gemma4:e4b-it-qat",
        }, None);
        var cleaner = h.Get<IDiskCleaner>();

        var report = await cleaner.ReportAsync(None);

        var used = report.Items.Single(i => i.Key == "component:ollama-model.gemma4:e4b-it-qat");
        used.State.ShouldBe(DiskItemState.Used);
        used.Area.ShouldBe(DiskArea.Models);
        used.UsedBy.ShouldBe([new DiskUse("Ottoman", PipelineSteps.Concepts)]);
        report.Items.Single(i => i.Key == "component:" + ComponentCodes.AppOllama).State.ShouldBe(DiskItemState.Used, "the program is used by the model");
        var unused = report.Items.Single(i => i.Key == "component:ollama-model.qwen3.5:4b-q4_K_M");
        unused.State.ShouldBe(DiskItemState.Unused);
        unused.Bytes.ShouldBeGreaterThan(0);
        report.Items.ShouldNotContain(i => i.Key == "tools:unowned" && i.Bytes > 0, "the models folder is owned, not a leftover");
        DiskSelection.Unused(report.Items).ShouldContain(unused);
        DiskSelection.Unused(report.Items).ShouldNotContain(used);
        DiskSelection.Full(report.Items).ShouldContain(used, "the full delete takes the app's models too");

        await h.Get<IAppOllamaServer>().StartAsync(None);
        var outcome = await cleaner.RemoveAsync([unused], None);

        outcome.Removed.ShouldBe([unused]);
        launcher.Started.ShouldHaveSingleItem().Stops.ShouldBe(1, "the app's server is ended before its files are taken");
        bin.Moved.ShouldContain(store.ManifestPath("qwen3.5:4b-q4_K_M"));
        File.Exists(store.BlobPath(shared[2])!).ShouldBeTrue("the layer the other model uses stays");
        store.IsInstalled("gemma4:e4b-it-qat").ShouldBeTrue();
        await h.Get<ComponentPlanner>().RefreshAllAsync(None);
        h.Get<IModelCatalog>().Find(ModelProviders.AppOllama, "qwen3.5:4b-q4_K_M").ShouldBeNull();
    }

    internal sealed class FakeBin : IRecycleBin
    {
        public List<string> Moved { get; } = [];

        public Task MoveAsync(string folder, CancellationToken cancellationToken)
        {
            Moved.Add(folder);
            if (File.Exists(folder))
            {
                File.Delete(folder);
            }
            else if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return Task.CompletedTask;
        }
    }
}

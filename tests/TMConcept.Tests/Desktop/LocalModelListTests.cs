using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Core;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The local models in the pickers (the developer, 18 Nov 2026: "Ollama and LM Studio models are not listed"): the user's own Ollama found
/// on this computer is added with its models, the app's Ollama catalogue is offered before anything is installed (and installed, with its
/// program first, only after the confirmation when a run starts), and every list puts what this card can run before what it cannot. A fake
/// probe stands for the user's server: no test talks to port 11434.
/// </summary>
public sealed class LocalModelListTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static DesktopHarness Harness(FakeConfirm confirm, FakeAppOllamaApi api, FakeProbe? probe = null) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(confirm);
        s.AddSingleton<IGpuInfo>(new FakeGpu(6L << 30));
        s.AddSingleton<IAppOllamaLauncher>(new FakeAppOllamaLauncher());
        s.AddSingleton<IAppOllamaApi>(api);
        if (probe is not null)
        {
            s.AddSingleton<IEndpointProbe>(probe);
        }
    });

    private static async Task<Profile> ProfileAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        return profile;
    }

    [AvaloniaFact]
    public async Task TheUsersOwnOllamaIsFoundAndItsModelsAreOfferedOnce()
    {
        var probe = new FakeProbe();
        using var h = Harness(new FakeConfirm(), new FakeAppOllamaApi(), probe);
        var profile = await ProfileAsync(h);
        var picker = h.Get<StepPickerViewModel>();

        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);

        // The look runs in the background (0.1.40): the rows are loaded again when it found the server.
        await picker.LocalServersLooked;
        var server = h.Get<EndpointRegistry>().All.ShouldHaveSingleItem();
        server.Name.ShouldBe("Ollama");
        server.Kind.ShouldBe(EndpointKind.Ollama);
        server.Models.Select(m => m.Id).ShouldBe(["llama3.2:3b", "gemma3:4b"]);
        var options = picker.Row(PipelineSteps.Concepts)!.Options.Where(o => o.Model is { } m && EndpointCodes.IsEndpoint(m.Provider)).ToList();
        options.Select(o => o.Model!.Id).ShouldBe(["gemma3:4b", "llama3.2:3b"], ignoreOrder: true);
        options.ShouldAllBe(o => o.Label.Contains(h.Get<Localizer>()["models.endpoint_free"], StringComparison.Ordinal));

        // Loaded again at once: not looked for again; looked for again later, the saved server is left as it is.
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);
        await picker.LocalServersLooked;
        probe.Detections.ShouldBe(1);
        (await h.Get<LocalServerSync>().SyncAsync(force: true, None)).ShouldBe(0);
        probe.Detections.ShouldBe(2);
        h.Get<EndpointRegistry>().All.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task AnAppModelNotInstalledYetIsOfferedAndInstalledWithItsProgramOnlyAfterTheConfirmation()
    {
        var confirm = new FakeConfirm { Answer = false };
        var api = new FakeAppOllamaApi();
        using var h = Harness(confirm, api);
        var profile = await ProfileAsync(h);
        var text = h.Get<Localizer>();
        var picker = h.Get<StepPickerViewModel>();
        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.ConceptPage);
        var row = picker.Row(PipelineSteps.Concepts)!;

        var option = row.Options.Single(o => o.Model is { Provider: ModelProviders.AppOllama, Id: "gemma3:4b" });
        option.Label.ShouldContain(text["models.not_installed"]);
        option.Label.ShouldContain(text["models.license.badge.limited"], Case.Sensitive, "Gemma's own terms");
        row.Selected = option;

        // Gemma's terms are accepted; the download list is declined.
        confirm.Answers.Enqueue(true);
        (await picker.PrepareAsync([PipelineSteps.Concepts], "install.purpose.concepts", None)).ShouldBeFalse("declined");

        api.Pulls.ShouldBeEmpty("nothing is downloaded without the confirmation");
        var list = confirm.Messages.Last();
        list.ShouldContain(AppOllamaDefaults.Name);
        list.ShouldContain("gemma3:4b");
        list.IndexOf(AppOllamaDefaults.Name, StringComparison.Ordinal).ShouldBeLessThan(list.IndexOf("gemma3:4b", StringComparison.Ordinal),
            "the program comes first in the same list");
        h.Get<AppModelRegistry>().Installed.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task WhatThisCardCanRunComesFirstAndWhatItCannotRunGoesToTheBottom()
    {
        using var h = Harness(new FakeConfirm(), new FakeAppOllamaApi());
        var profile = await ProfileAsync(h);
        var picker = h.Get<StepPickerViewModel>();

        await picker.LoadAsync(profile.Id, ContentLanguages.English, PipelineSteps.VideoPage);

        foreach (var row in picker.Rows)
        {
            var models = row.Options.Where(o => o.Model is not null).ToList();
            var lastRunnable = models.FindLastIndex(o => !o.IsError);
            var firstNot = models.FindIndex(o => o.IsError);
            if (firstNot >= 0)
            {
                firstNot.ShouldBeGreaterThan(lastRunnable, row.Step.Code + ": every entry this card cannot run stands below the others");
            }
        }

        // The 6 GB card: a small model of the app's Ollama fits (green), a 27B one does not (red, at the bottom).
        var code = picker.Row(PipelineSteps.VideoCode)!.Options;
        code.Single(o => o.Model?.Id == "qwen3.5:4b-q4_K_M").IsOk.ShouldBeTrue();
        code.Single(o => o.Model?.Id == "gemma3:4b").IsError.ShouldBeFalse("it fits; its license badge makes it yellow, not red");
        var large = code.Single(o => o.Model?.Id == "qwen3.5:27b-q4_K_M");
        large.IsError.ShouldBeTrue();
        large.Label.ShouldContain(h.Get<Localizer>()["models.fit.too_large"]);
        code.IndexOf(large).ShouldBeGreaterThan(code.IndexOf(code.First(o => o.Model?.Provider == "anthropic")), "below the online models too");
    }

    /// <summary>The user's Ollama on this computer, with two models.</summary>
    internal sealed class FakeProbe : IEndpointProbe
    {
        public int Detections { get; private set; }

        public Task<IReadOnlyList<DetectedServer>> DetectAsync(CancellationToken cancellationToken)
        {
            Detections++;
            return Task.FromResult<IReadOnlyList<DetectedServer>>([new DetectedServer(EndpointKind.Ollama, "http://127.0.0.1:11434", "0.13.0", 2)]);
        }

        public Task<EndpointListing> ListModelsAsync(string baseUrl, string token, CancellationToken cancellationToken) =>
            Task.FromResult(new EndpointListing(true, string.Empty, string.Empty,
            [
                new ListedModel("llama3.2:3b", 8_192, "Llama 3.2 3B", JsonSchema: true, SizeBytes: 2_000_000_000),
                new ListedModel("gemma3:4b", 8_192, "Gemma 3 4B", Vision: true, JsonSchema: true, SizeBytes: 3_300_000_000),
            ], EndpointKind.Ollama, "http://127.0.0.1:11434", "0.13.0"));

        public Task<IReadOnlyList<LoadedServerModel>> LoadedAsync(LocalEndpoint endpoint, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LoadedServerModel>>([]);
    }
}

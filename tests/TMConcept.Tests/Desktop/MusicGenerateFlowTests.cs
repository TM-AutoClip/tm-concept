using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.55: "Üret" on the "Müzik üret" page works like every other page (the developer: "kuruluysa kullanmaya başlıyoruz, değilse kurulum onayı
/// istiyoruz"; 0.1.54 stopped with "download the model first" and had an install button under the model). A local model that is not installed
/// can be chosen; "Üret" asks ONE confirmation listing its downloads — declined: nothing is made; accepted: it is installed and the piece is
/// made at once. An online model without a key opens the key window; without a key nothing is sent. A paid piece is a run of its own, so the
/// month's spending counts it. Fake components, fake models: nothing is downloaded, nothing reaches the network, no model runs.
/// </summary>
public sealed class MusicGenerateFlowTests
{
    private static DesktopHarness Harness(FakeConfirm confirm, MusicPageTests.FakeGenerator generator, FakeComponent environment, FakeComponent weights,
        Func<IApiKeyStore, FakeApiKeyDialog>? keys = null) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(confirm);
        s.RemoveAll<IComponentSource>();
        s.AddSingleton<IComponentSource>(new FakeComponents(environment, weights, new FakeComponent(ComponentCodes.Ffmpeg, ready: true)));
        s.RemoveAll<IMusicGenerators>();
        s.AddSingleton<IMusicGenerators>(sp => new InstalledWhenComponentsAre(new MusicGenerators(sp.GetRequiredService<IModelCatalog>(), [],
            sp.GetRequiredService<StepModelResolver>()), generator, environment, weights));
        if (keys is not null)
        {
            s.AddSingleton<IApiKeyDialog>(sp => keys(sp.GetRequiredService<IApiKeyStore>()));
        }
    });

    [AvaloniaFact]
    public async Task ALocalModelNotInstalledCanBeChosenAndDecliningTheOneConfirmationMakesNothing()
    {
        var confirm = new FakeConfirm { Answer = false };
        var generator = new MusicPageTests.FakeGenerator();
        var environment = new FakeComponent(ComponentCodes.MusicEnvironment, ready: false);
        var weights = new FakeComponent(ComponentCodes.MusicModel, ready: false, ComponentCodes.MusicEnvironment);
        using var h = Harness(confirm, generator, environment, weights);
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();
        confirm.Asked.Clear();

        page.Model = page.Models.Single(m => m.Key == "local/ace-step-1.5");
        page.Model.Ready.ShouldBeFalse();
        page.Model.Label.ShouldContain(text["models.not_installed"]);
        typeof(MusicViewModel).GetProperty("InstallLocalCommand").ShouldBeNull("no install button under the model");
        typeof(MusicViewModel).GetProperty("NeedsLocalInstall").ShouldBeNull();
        page.Prompt = "calm sea";
        page.GenerateCommand.CanExecute(null).ShouldBeTrue("a model that is not installed is installed when it starts");

        await page.GenerateCommand.ExecuteAsync(null);

        confirm.Asked.Count.ShouldBe(1, "one confirmation listing every download");
        confirm.Messages[0].ShouldContain(environment.Name);
        confirm.Messages[0].ShouldContain(weights.Name);
        confirm.Messages[0].ShouldContain(text["install.purpose.music_make"]);
        page.Status.ShouldBe(text.Format("picker.not_started.install", text["install.declined"]));
        (environment.Installs + weights.Installs).ShouldBe(0);
        generator.Requests.ShouldBeEmpty();
        page.Pieces.ShouldBeEmpty();
        page.Busy.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task AcceptingInstallsTheModelAndMakesThePieceAtOnce()
    {
        var confirm = new FakeConfirm();
        var generator = new MusicPageTests.FakeGenerator();
        var environment = new FakeComponent(ComponentCodes.MusicEnvironment, ready: false);
        var weights = new FakeComponent(ComponentCodes.MusicModel, ready: false, ComponentCodes.MusicEnvironment);
        using var h = Harness(confirm, generator, environment, weights);
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();
        page.Model = page.Models.Single(m => m.Key == "local/ace-step-1.5");
        page.Prompt = "calm sea";

        await page.GenerateCommand.ExecuteAsync(null);

        environment.Installs.ShouldBe(1);
        weights.Installs.ShouldBe(1);
        generator.Requests.ShouldHaveSingleItem().Prompt.ShouldStartWith("calm sea");
        page.Pieces.ShouldHaveSingleItem();
        page.Status.ShouldBe(text.Format("music.made", "calm sea"));
        page.Model.ShouldNotBeNull().Ready.ShouldBeTrue("the list no longer says \"not installed\"");
        page.Model.Label.ShouldNotContain(text["models.not_installed"]);

        // Installed now: the next press asks nothing.
        var asked = confirm.Asked.Count;
        await page.GenerateCommand.ExecuteAsync(null);
        confirm.Asked.Count.ShouldBe(asked);
        generator.Requests.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task AnOnlineModelWithoutAKeyOpensTheKeyWindowAndAPaidPieceIsARun()
    {
        FakeApiKeyDialog? dialog = null;
        var generator = new MusicPageTests.FakeGenerator { Usd = 0.15m };
        using var h = Harness(new FakeConfirm(), generator, new FakeComponent(ComponentCodes.MusicEnvironment, ready: true),
            new FakeComponent(ComponentCodes.MusicModel, ready: true), keys => dialog = new FakeApiKeyDialog(keys));
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();
        page.Model = page.Models.Single(m => m.Key == "elevenlabs/music_v1");
        page.Prompt = "epic drums";

        await page.GenerateCommand.ExecuteAsync(null);

        dialog.ShouldNotBeNull().Asked.ShouldBe(["elevenlabs"]);
        page.Status.ShouldBe(text.Format("picker.not_started.key", "elevenlabs"));
        generator.Requests.ShouldBeEmpty("nothing is sent without a key");

        dialog.Secret = "sk-test";
        await page.GenerateCommand.ExecuteAsync(null);

        generator.Requests.ShouldHaveSingleItem();
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).ShouldHaveSingleItem();
        run.Kind.ShouldBe(RunKind.Music);
        run.Provider.ShouldBe("elevenlabs");
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.ActualUsd.ShouldBe(0.15m, "the month's spending counts the piece");
    }

    /// <summary>The catalog's music models made by the fake; the local one is installed when its two components are.</summary>
    private sealed class InstalledWhenComponentsAre(IMusicGenerators inner, MusicPageTests.FakeGenerator generator, FakeComponent environment,
        FakeComponent weights) : IMusicGenerators
    {
        public IReadOnlyList<CatalogModel> Models => inner.Models;

        public CatalogModel? Find(string key) => inner.Find(key);

        public bool Ready(CatalogModel model) => model.Provider != ModelProviders.Local || (environment.Ready && weights.Ready);

        public Task<ResolvedModel> ResolveAsync(CatalogModel model, CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedModel(model, new ApiKey { Provider = model.Provider, Label = "test" }, string.Empty));

        public IMusicGenerator? For(CatalogModel model) => generator;
    }
}

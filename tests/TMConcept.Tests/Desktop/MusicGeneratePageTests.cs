using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Generated music in the video page's music choice (0.1.46, C5; 0.1.54: moved there from Settings → Templates; 0.1.55: a music model is
/// chosen like every other model) with fake hardware and fake models: a local model that is not installed yet can be chosen, says so in its
/// label and is installed when the video starts (no install button of its own); a model made for an NVIDIA card says "(yalnız NVIDIA)" on
/// another card and, once chosen, shows the yellow "this card is not supported" note without blocking the choice; a model that runs on the
/// processor never carries that note; the online models list with their prices and the chosen one is the profile's music model. Nothing is
/// installed, nothing reaches the network.
/// </summary>
public sealed class MusicGeneratePageTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static readonly GpuReport Amd = new([new GpuAdapter(GpuVendor.Amd, "Radeon", 16 * GiB)], false, true);

    private static readonly GpuReport Nvidia = new([new GpuAdapter(GpuVendor.Nvidia, "RTX", 8 * GiB)], true, true);

    private static async Task<(DesktopHarness H, VideoMediaViewModel Editor)> EditorAsync(GpuReport card, bool localInstalled)
    {
        var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(new FakeConfirm());
            s.RemoveAll<IGpuInfo>();
            s.AddSingleton<IGpuInfo>(new FakeGpu(card));
            s.RemoveAll<IMusicGenerators>();
            s.AddSingleton<IMusicGenerators>(sp => new InstalledOrNot(new MusicGenerators(sp.GetRequiredService<IModelCatalog>(), [],
                sp.GetRequiredService<StepModelResolver>()), localInstalled));
        });
        await FirstRunTests.CreateAsync(h);
        var media = h.Get<VideoMediaViewModel>();
        await media.LoadAsync(new VideoMedia());
        return (h, media);
    }

    [AvaloniaFact]
    public async Task ALocalModelNotInstalledYetIsChosenLikeAnyOtherModelAndInstalledWhenTheVideoStarts()
    {
        var (h, editor) = await EditorAsync(Nvidia, localInstalled: false);
        using var _ = h;
        var text = h.Get<Localizer>();

        var local = editor.MusicSources.Single(c => c.Value == (int)MusicSource.GenerateLocal);
        local.Enabled.ShouldBeTrue("a model is never refused for not being installed: it is installed when the video starts");
        local.Label.ShouldBe(text["style.music.generatelocal"], "no model's name and no card note on the source itself");
        typeof(VideoMediaViewModel).GetProperty("InstallLocalMusicCommand").ShouldBeNull("no install button of its own");

        editor.MusicSource = local;

        editor.MusicSource.Value.ShouldBe((int)MusicSource.GenerateLocal);
        editor.MusicNote.ShouldBeEmpty();
        var ace = editor.LocalMusicModels.Single(o => o.Key == "local/ace-step-1.5");
        ace.Label.ShouldContain(text["models.not_installed"]);
        ace.Label.ShouldNotContain(text["style.music.generatelocal.nvidia_only"], Case.Sensitive, "an NVIDIA card");
        editor.LocalMusicModel.ShouldBe(ace);
        editor.MusicCardWarning.ShouldBeEmpty();
        var model = editor.GeneratedModel.ShouldNotBeNull("the video makes this model ready before it starts");
        model.Requires.ShouldBe(["speech-env.ace-step", "music.ace-step-1.5"], "its downloads go into the video's one confirmation");
        editor.ToMedia().Music.Generator.ShouldBe("local/ace-step-1.5");
    }

    [AvaloniaFact]
    public async Task OnAnotherCardTheCardModelIsLabelledAndWarnedButNotBlocked()
    {
        var (h, editor) = await EditorAsync(Amd, localInstalled: true);
        using var _ = h;
        var text = h.Get<Localizer>();

        var local = editor.MusicSources.Single(c => c.Value == (int)MusicSource.GenerateLocal);
        local.Enabled.ShouldBeTrue("the choice is never blocked by the card");
        editor.LocalMusicModels.Single(o => o.Key == "local/ace-step-1.5").Label.ShouldContain(text["style.music.generatelocal.nvidia_only"]);
        editor.LocalMusicModels.Single(o => o.Key == "local/ace-step-1.5").Label.ShouldNotContain(text["models.not_installed"]);
        editor.MusicCardWarning.ShouldBeEmpty("nothing to say while another source is chosen");
        editor.LocalMusicModel.ShouldNotBeNull().Key.ShouldBe("local/" + ScoreRules.ModelId, "without NVIDIA the first choice is a model that runs here");

        editor.MusicSource = local;

        editor.MusicSource.Value.ShouldBe((int)MusicSource.GenerateLocal);
        editor.MusicCardWarning.ShouldBeEmpty("the composed music runs on any computer");

        editor.LocalMusicModel = editor.LocalMusicModels.Single(o => o.Key == "local/ace-step-1.5");

        editor.MusicCardWarning.ShouldContain(text["styles.music.generate.nvidia_warning"]);
        var style = editor.ToMedia();
        style.Music.Source.ShouldBe(MusicSource.GenerateLocal);
        style.Music.Generator.ShouldBe("local/ace-step-1.5");
        style.Music.Instrumental.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task OnAnNvidiaCardThereIsNoWarning()
    {
        var (h, editor) = await EditorAsync(Nvidia, localInstalled: true);
        using var _ = h;

        editor.MusicSource = editor.MusicSources.Single(c => c.Value == (int)MusicSource.GenerateLocal);

        editor.MusicSource.Value.ShouldBe((int)MusicSource.GenerateLocal);
        editor.MusicCardWarning.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task AModelAboveTheCardsClassIsRedAndANonCommercialOneCarriesTheOrangeBadge()
    {
        // 28 Nov 2026: the developer's 6 GB card; MusicGen Small fits (4 GB class), MusicGen Large does not (12 GB class); both non-commercial.
        using var h = new DesktopHarness();
        var labels = h.Get<IMusicChoiceText>();
        var text = h.Get<Localizer>();
        var catalog = h.Get<IModelCatalog>();
        var card = new LocalMusicCard(Nvidia: true, Linux: false, VramTier.Gb6);
        var small = catalog.Find(ModelProviders.Local, "musicgen-small")!;
        var large = catalog.Find(ModelProviders.Local, "musicgen-large")!;

        var smallLabel = labels.ModelLabel(small, ready: false, card);
        smallLabel.ShouldContain(text.Format("models.tier", 4));
        smallLabel.ShouldContain(text["models.license.badge.noncommercial"]);
        smallLabel.ShouldNotContain(text["models.tier.too_big"]);
        labels.CardWarning(small, card).ShouldBeEmpty();
        var largeLabel = labels.ModelLabel(large, ready: false, card);
        largeLabel.ShouldContain(text.Format("models.tier", 12));
        largeLabel.ShouldContain(text["models.tier.too_big"]);
        labels.CardWarning(large, card).ShouldBe(text.Format("styles.music.generate.too_big", 12, 6), "said, never a block (rule 19)");
        labels.ModelLabel(catalog.Find(ModelProviders.Local, "ace-step-1.5")!, ready: true, card).ShouldNotContain(text["models.license.badge.noncommercial"]);
        var order = h.Get<IModelListOrder>();
        order.Order([large, small], m => order.GroupOf(m, card.CannotRun(m))).ShouldBe([small, large], "the ones this card holds first");
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task AProcessorModelNeverCarriesTheCardNote()
    {
        using var h = new DesktopHarness();
        var labels = h.Get<IMusicChoiceText>();
        var text = h.Get<Localizer>();
        var processor = new CatalogModel { Provider = ModelProviders.Local, Id = "cpu-music", Name = "CPU music", Capabilities = ModelCapability.Music };
        var noNvidiaOnLinux = new LocalMusicCard(Nvidia: false, Linux: true);

        labels.ModelLabel(processor, ready: false, noNvidiaOnLinux).ShouldBe("CPU music · " + text["models.not_installed"]);
        labels.CardWarning(processor, noNvidiaOnLinux).ShouldBeEmpty();
        LocalMusicSupport.NeedsCard(processor).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task TheOnlineModelsListWithTheirPricesAndTheChosenOneIsKept()
    {
        var (h, editor) = await EditorAsync(Amd, localInstalled: false);
        using var _ = h;
        var text = h.Get<Localizer>();

        editor.MusicSource = editor.MusicSources.Single(c => c.Value == (int)MusicSource.GenerateOnline);

        editor.MusicGenerateOnline.ShouldBeTrue();
        editor.MusicCardWarning.ShouldBeEmpty("the card matters only for a local model");
        editor.OnlineMusicModels.Select(o => o.Key).ShouldBe(["google/lyria-3-clip-preview", "google/lyria-3.5", "elevenlabs/music_v1"], ignoreOrder: true);
        editor.OnlineMusicModels.Single(o => o.Key == "google/lyria-3-clip-preview").Label.ShouldContain(text.Usd(0.04m));
        editor.OnlineMusicModels.Single(o => o.Key == "elevenlabs/music_v1").Label.ShouldContain(text.Usd(0.15m));

        editor.OnlineMusicModel = editor.OnlineMusicModels.Single(o => o.Key == "elevenlabs/music_v1");

        editor.ToMedia().Music.Generator.ShouldBe("elevenlabs/music_v1");
        editor.GeneratedModel.ShouldNotBeNull().Id.ShouldBe("music_v1", "its key is asked before the video starts");
        VideoStyleRules.MediaProblems(editor.ToMedia()).ShouldNotContain(VideoStyleProblem.MusicModelMissing);
    }

    [AvaloniaFact]
    public async Task TheMusicPageListsWhatRunsHereThenOnlineThenRedAndChoosesTheFirstThatRuns()
    {
        // 0.1.60, the plan's step 2: on the developer's 6 GB card the 8 GB YuE 7B and the larger models come after the online ones.
        using var h = PageHarness(new GpuReport([new GpuAdapter(GpuVendor.Nvidia, "RTX 3060 Laptop", 6 * GiB)], true, true));
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();

        var groups = page.Models.Select(m => m.Group).ToList();
        groups.ShouldBe(groups.Order().ToList(), "runs here, online, red: never mixed");
        groups.ShouldContain(ModelListGroup.Runs);
        groups.ShouldContain(ModelListGroup.Online);
        page.Models.Last(m => m.Group == ModelListGroup.Online).ShouldNotBe(page.Models[^1], "red ones after the online ones");
        page.Models.Single(m => m.Key == "local/yue-v1-exl2").IsError.ShouldBeTrue("8 GB class on a 6 GB card");
        page.Models.Where(m => !m.Local).ShouldAllBe(m => m.Group == ModelListGroup.Online);
        page.Model.ShouldNotBeNull().Group.ShouldBe(ModelListGroup.Runs, "nothing chosen: the first model that runs here");
        page.Model.ShouldBe(page.Models[0]);

        // The catalog's order inside each group.
        var catalog = h.Get<IMusicGenerators>().Models.Select(ModelBase.KeyOf).ToList();
        foreach (var group in Enum.GetValues<ModelListGroup>())
        {
            var keys = page.Models.Where(m => m.Group == group).Select(m => m.Key).ToList();
            keys.ShouldBe(catalog.Where(keys.Contains).ToList());
        }
    }

    [AvaloniaFact]
    public async Task WithoutNvidiaTheCardModelsAreRedAndLastAndProgramComposesIsChosen()
    {
        using var h = PageHarness(Amd);
        await FirstRunTests.CreateAsync(h);
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();

        page.Model.ShouldNotBeNull().Key.ShouldBe("local/" + ScoreRules.ModelId);
        page.Models.Where(m => m.Local && m.Key != "local/" + ScoreRules.ModelId).ShouldAllBe(m => m.IsError, "they need an NVIDIA card");
        page.Models.Where(m => m.IsError).ShouldAllBe(m => !m.IsCaution && !m.IsTight, "red wins over orange and yellow");
        var groups = page.Models.Select(m => m.Group).ToList();
        groups.ShouldBe(groups.Order().ToList());
        page.Models[1].Group.ShouldBe(ModelListGroup.Online, "the online ones right after Program composes");

        // A red one is still offered and can be chosen (rule 19), with its note.
        page.Model = page.Models.First(m => m.Key == "local/ace-step-1.5");
        page.CardWarning.ShouldContain(h.Get<Localizer>()["styles.music.generate.nvidia_warning"]);
    }

    [AvaloniaFact]
    public async Task AModelOfExactlyTheCardsClassIsYellowTight()
    {
        // K13: YuE 7B (8 GB class) on an 8 GB card; MusicGen Small (4 GB) is not tight, MusicGen Large (12 GB) is red, not tight.
        using var h = PageHarness(Nvidia);
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var page = h.Get<MusicViewModel>();
        await page.ActivateAsync();

        var yue = page.Models.Single(m => m.Key == "local/yue-v1-exl2");
        yue.IsError.ShouldBeFalse();
        yue.Group.ShouldBe(ModelListGroup.Runs);
        yue.Label.ShouldContain(text["models.tier.tight"]);
        (yue.IsTight || yue.IsCaution).ShouldBeTrue("yellow, or orange when its licence is non-commercial (orange wins)");
        page.Models.Single(m => m.Key == "local/musicgen-small").Label.ShouldNotContain(text["models.tier.tight"]);
        var large = page.Models.Single(m => m.Key == "local/musicgen-large");
        large.IsError.ShouldBeTrue();
        large.IsTight.ShouldBeFalse();
        large.Label.ShouldNotContain(text["models.tier.tight"]);

        page.Model = yue;
        page.CardWarning.ShouldBe(text.Format("styles.music.generate.tight", 8));
        text["models.tier.tight"].ShouldNotBeNullOrWhiteSpace();
    }

    [AvaloniaFact]
    public async Task TheVideoPagesLocalModelsPutRedLastAndChooseTheFirstThatRuns()
    {
        var (h, editor) = await EditorAsync(new GpuReport([new GpuAdapter(GpuVendor.Nvidia, "RTX 3060 Laptop", 6 * GiB)], true, true), localInstalled: false);
        using var _ = h;

        var red = editor.LocalMusicModels.Select(o => o.IsError).ToList();
        red.ShouldContain(true);
        red.ShouldBe(red.Order().ToList(), "every red one after every one that runs");
        editor.LocalMusicModel.ShouldNotBeNull().IsError.ShouldBeFalse();
        editor.OnlineMusicModels.ShouldAllBe(o => o.Group == ModelListGroup.Online);
    }

    private static DesktopHarness PageHarness(GpuReport card) => new(s =>
    {
        s.AddSingleton<IConfirmDialog>(new FakeConfirm());
        s.RemoveAll<IGpuInfo>();
        s.AddSingleton<IGpuInfo>(new FakeGpu(card));
        s.RemoveAll<IMusicGenerators>();
        s.AddSingleton<IMusicGenerators>(sp => new InstalledOrNot(new MusicGenerators(sp.GetRequiredService<IModelCatalog>(), [],
            sp.GetRequiredService<StepModelResolver>()), localInstalled: false));
    });

    /// <summary>The catalog's music models; the local one counts as installed or not.</summary>
    private sealed class InstalledOrNot(IMusicGenerators inner, bool localInstalled) : IMusicGenerators
    {
        public IReadOnlyList<CatalogModel> Models => inner.Models;

        public CatalogModel? Find(string generator) => inner.Find(generator);

        public bool Ready(CatalogModel model) => model.Provider != ModelProviders.Local || localInstalled;

        public Task<ResolvedModel> ResolveAsync(CatalogModel model, CancellationToken cancellationToken) => inner.ResolveAsync(model, cancellationToken);

        public IMusicGenerator? For(CatalogModel model) => null;
    }
}

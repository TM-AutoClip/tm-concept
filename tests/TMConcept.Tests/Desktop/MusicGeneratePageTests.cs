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
/// Generated music in the video page's music choice (0.1.46, C5; 0.1.54: moved there from Settings → Templates) with fake hardware and fake models: "Üret (yerel)" is greyed out and refused while the
/// local model is not installed; on a card that is not NVIDIA it says "(yalnız NVIDIA)" and, once chosen, shows the yellow "this card is not
/// supported" note without blocking the choice; on an NVIDIA card neither shows; the online models list with their prices and the chosen one
/// is the profile's music model. Nothing is installed, nothing reaches the network.
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
    public async Task TheLocalModelCannotBeChosenUntilItIsInstalled()
    {
        var (h, editor) = await EditorAsync(Nvidia, localInstalled: false);
        using var _ = h;
        var text = h.Get<Localizer>();

        var local = editor.MusicSources.Single(c => c.Value == (int)MusicSource.GenerateLocal);
        local.Enabled.ShouldBeFalse("the model is not on this computer");
        local.Label.ShouldBe(text["style.music.generatelocal"], "an NVIDIA card: no label");
        editor.CanInstallLocalMusic.ShouldBeTrue();
        editor.InstallLocalMusicCommand.CanExecute(null).ShouldBeTrue();
        editor.LocalMusicInstallText.ShouldContain("23");
        editor.LocalMusicInstallText.ShouldContain("16");

        editor.MusicSource = local;

        editor.MusicSource.Value.ShouldBe((int)MusicSource.Off, "refused");
        editor.MusicNote.ShouldBe(text["styles.music.generate.local.not_ready"]);
        editor.MusicCardWarning.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task OnAnotherCardTheLocalModelIsLabelledAndWarnedButNotBlocked()
    {
        var (h, editor) = await EditorAsync(Amd, localInstalled: true);
        using var _ = h;
        var text = h.Get<Localizer>();

        var local = editor.MusicSources.Single(c => c.Value == (int)MusicSource.GenerateLocal);
        local.Label.ShouldContain(text["style.music.generatelocal.nvidia_only"]);
        local.Enabled.ShouldBeTrue("the choice is never blocked by the card");
        editor.MusicCardWarning.ShouldBeEmpty("nothing to say while another source is chosen");

        editor.MusicSource = local;

        editor.MusicSource.Value.ShouldBe((int)MusicSource.GenerateLocal);
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
        editor.CanInstallLocalMusic.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task TheOnlineModelsListWithTheirPricesAndTheChosenOneIsKept()
    {
        var (h, editor) = await EditorAsync(Amd, localInstalled: false);
        using var _ = h;
        var text = h.Get<Localizer>();

        editor.MusicSource = editor.MusicSources.Single(c => c.Value == (int)MusicSource.GenerateOnline);

        editor.MusicGenerateOnline.ShouldBeTrue();
        editor.MusicCardWarning.ShouldBeEmpty("the card matters only for the local model");
        editor.OnlineMusicModels.Select(o => o.Key).ShouldBe(["google/lyria-3-clip-preview", "google/lyria-3.5", "elevenlabs/music_v1"], ignoreOrder: true);
        editor.OnlineMusicModels.Single(o => o.Key == "google/lyria-3-clip-preview").Label.ShouldContain(text.Usd(0.04m));
        editor.OnlineMusicModels.Single(o => o.Key == "elevenlabs/music_v1").Label.ShouldContain(text.Usd(0.15m));

        editor.OnlineMusicModel = editor.OnlineMusicModels.Single(o => o.Key == "elevenlabs/music_v1");

        editor.ToMedia().Music.Generator.ShouldBe("elevenlabs/music_v1");
        VideoStyleRules.MediaProblems(editor.ToMedia()).ShouldNotContain(VideoStyleProblem.MusicModelMissing);
    }

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

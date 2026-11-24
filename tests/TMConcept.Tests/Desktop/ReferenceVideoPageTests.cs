using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Core.References;
using TMConcept.Core.Speech;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The reference video of a template in Settings → Templates (S1, 0.1.52): a measured file sets the tempo to the nearest preset and is kept
/// in the template, its colours become the own palette only on "Use the colours", "Remove" takes it out, a failure is said on the form and in
/// the activity panel, Cancel keeps nothing, and an address is not measured when the install of ffmpeg and yt-dlp is declined. A fake measure
/// only: nothing is downloaded or run.
/// </summary>
public sealed class ReferenceVideoPageTests
{
    private static readonly ReferenceTempoProfile Fast = new()
    {
        Source = "clip.mp4", Seconds = 95, Cuts = 60, CutsPer10Seconds = 6.3, AverageShotSeconds = 1.6, CameraShare = 0.7, CameraDirection = CameraDirection.Left,
        Ease = EaseShape.Out, LabL = 40, Brightness = 0.4, Saturation = 0.5, Palette = ["#202830", "#D04020", "#E0C080"], HasSound = true, Bpm = 128,
        Peaks = [0.3, 0.8],
    };

    [AvaloniaFact]
    public async Task AMeasuredFileSetsTheTempoAndIsKeptInTheTemplate()
    {
        using var h = new DesktopHarness();
        var text = h.Get<Localizer>();
        var measurer = new FakeMeasurer { Result = Fast };
        var activity = new Lines();
        var editor = new VideoStyleEditorViewModel(new VideoStyle { Name = "A", Tempo = NarrationTempo.Relaxed }, true, text,
            referenceServices: new ReferenceVideoServices(measurer, () => Task.FromResult<string?>("C:/videos/clip.mp4"), _ => Task.FromResult(true), activity));
        var reference = editor.ReferenceVideo.ShouldNotBeNull();
        reference.HasProfile.ShouldBeFalse();
        reference.RemoveCommand.CanExecute(null).ShouldBeFalse();

        await reference.MeasureFileCommand.ExecuteAsync(null);

        measurer.Files.ShouldBe(["C:/videos/clip.mp4"]);
        reference.HasProfile.ShouldBeTrue();
        editor.Tempo.Value.ShouldBe((int)NarrationTempo.Tight, "6.3 cuts every 10 s is nearest to tight");
        reference.Status.ShouldBe(text.Format("styles.reference.measured", text["style.tempo.tight"]));
        reference.TempoText.ShouldContain(text["styles.reference.direction.left"]);
        reference.SoundText.ShouldContain("128");
        reference.Swatches.Count.ShouldBe(3);
        activity.Entries.ShouldContain(e => e.Level == ActivityLevel.Info);
        var style = editor.ToStyle();
        style.Reference.ShouldNotBeNull().CutsPer10Seconds.ShouldBe(6.3);
        style.Tempo.ShouldBe(NarrationTempo.Tight);
        style.PaletteId.ShouldNotBe(VideoPalettes.CustomId, "the colours are only a suggestion until taken");

        reference.UseColorsCommand.Execute(null);
        var taken = editor.ToStyle();
        taken.PaletteId.ShouldBe(VideoPalettes.CustomId);
        taken.CustomPalette.ShouldBe(ReferenceTempoMapper.Suggest(Fast).Palette! with { Id = VideoPalettes.CustomId });

        reference.RemoveCommand.Execute(null);
        editor.ToStyle().Reference.ShouldBeNull();
        editor.ToStyle().Tempo.ShouldBe(NarrationTempo.Tight, "the tempo stays as it is");
    }

    [AvaloniaFact]
    public async Task AFailureIsSaidAndACancelKeepsNothing()
    {
        using var h = new DesktopHarness();
        var text = h.Get<Localizer>();
        var measurer = new FakeMeasurer { Error = new ReferenceMeasureException("x") { Code = "download", Detail = "HTTP Error 403" } };
        var activity = new Lines();
        var source = new VideoStyle { Name = "A", Reference = Fast };
        var editor = new VideoStyleEditorViewModel(source, false, text,
            referenceServices: new ReferenceVideoServices(measurer, () => Task.FromResult<string?>(null), _ => Task.FromResult(true), activity));
        var reference = editor.ReferenceVideo.ShouldNotBeNull();
        reference.HasProfile.ShouldBeTrue("the template's reference is shown");

        reference.Address = "not an address";
        await reference.MeasureAddressCommand.ExecuteAsync(null);
        reference.Status.ShouldBe(text["styles.reference.error.address"]);
        measurer.Addresses.ShouldBeEmpty();

        reference.Address = "https://example.com/v";
        await reference.MeasureAddressCommand.ExecuteAsync(null);
        reference.Status.ShouldBe(text["styles.reference.error.download"] + " (HTTP Error 403)");
        activity.Entries.ShouldContain(e => e.Level == ActivityLevel.Error && e.Text.Contains("HTTP Error 403", StringComparison.Ordinal));
        editor.ToStyle().Reference.ShouldBeSameAs(reference.Profile, "a failed measure keeps the reference the template had");

        measurer.Error = null;
        measurer.Wait = true;
        var running = reference.MeasureAddressCommand.ExecuteAsync(null);
        reference.Busy.ShouldBeTrue();
        reference.CancelCommand.Execute(null);
        await running;
        reference.Status.ShouldBe(text["styles.reference.cancelled"]);
        reference.Busy.ShouldBeFalse();
        reference.Profile!.Source.ShouldBe("clip.mp4");
    }

    [AvaloniaFact]
    public async Task AnAddressIsNotMeasuredWhenTheInstallIsDeclined()
    {
        var measurer = new FakeMeasurer { Result = Fast };
        var confirm = new FakeConfirm { Answer = false };
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(confirm);
            s.RemoveAll<IReferenceMeasurer>();
            s.AddSingleton<IReferenceMeasurer>(measurer);
        });
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var section = (await h.Get<SettingsViewModel>().ShowAsync<VideoStylesSettingsViewModel>()).ShouldNotBeNull();
        section.Add();
        var reference = section.Editor.ShouldNotBeNull().ReferenceVideo.ShouldNotBeNull();

        reference.Address = "https://example.com/v";
        await reference.MeasureAddressCommand.ExecuteAsync(null);

        confirm.Messages.ShouldContain(m => m.Contains("yt-dlp", StringComparison.Ordinal), "yt-dlp is listed in the one confirmation");
        measurer.Addresses.ShouldBeEmpty();
        reference.HasProfile.ShouldBeFalse();
        reference.Status.ShouldBe(text["styles.reference.not_installed.address"]);
    }

    private sealed class FakeMeasurer : IReferenceMeasurer
    {
        public ReferenceTempoProfile? Result { get; set; }

        public Exception? Error { get; set; }

        public bool Wait { get; set; }

        public List<string> Files { get; } = [];

        public List<string> Addresses { get; } = [];

        public bool DownloaderReady => true;

        public Task<ReferenceTempoProfile> MeasureFileAsync(string file, IProgress<ReferenceProgress>? progress, CancellationToken cancellationToken)
        {
            Files.Add(file);
            return AnswerAsync(progress, cancellationToken);
        }

        public Task<ReferenceTempoProfile> MeasureAddressAsync(string address, IProgress<ReferenceProgress>? progress, CancellationToken cancellationToken)
        {
            Addresses.Add(address);
            return AnswerAsync(progress, cancellationToken);
        }

        private async Task<ReferenceTempoProfile> AnswerAsync(IProgress<ReferenceProgress>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(new ReferenceProgress(ReferenceStages.Download, 0.5));
            if (Wait)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Error is { } error ? throw error : Result!;
        }
    }

    private sealed class Lines : IActivityLog
    {
        public List<(ActivityLevel Level, string Text)> Entries { get; } = [];

        public void Add(ActivityLevel level, string text) => Entries.Add((level, text));

        public void Add(ActivityLevel level, string text, string fileText) => Entries.Add((level, text));
    }
}

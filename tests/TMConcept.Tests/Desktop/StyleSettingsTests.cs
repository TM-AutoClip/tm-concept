using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>0.1.19 Settings → Templates: add, edit, cancel and delete (asked first), the limit of 20, the live preview and every label.</summary>
public sealed class StyleSettingsTests
{
    [AvaloniaFact]
    public async Task ATemplateIsAddedEditedAndDeletedAfterAskingAndTheFormShowsAPreview()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var settings = h.Get<SettingsViewModel>();
        var section = (await settings.ShowAsync<VideoStylesSettingsViewModel>()).ShouldNotBeNull();
        section.Title.ShouldBe(text["settings.styles"]);
        section.Empty.ShouldBeTrue();
        section.CountText.ShouldBe(text.Format("styles.count", 0, VideoStyleLimits.Max));

        section.Add();
        var editor = section.Editor.ShouldNotBeNull();
        editor.IsNew.ShouldBeTrue();
        editor.Name.ShouldBe(text["styles.new_name"]);
        (editor.Subtitles, editor.ProgressBar).ShouldBe((true, false), "subtitles on, progress bar off by default");
        editor.TitleCells.Count.ShouldBe(9);
        editor.TitleCells.Single(c => c.IsChecked).Position.ShouldBe(TitlePosition.TopCenter);
        editor.Palettes.Select(p => p.Palette.Id).ShouldBe(VideoPalettes.All.Select(p => p.Id));
        editor.Palettes.ShouldAllBe(p => p.Swatches.Count == PaletteTokens.All.Count);
        editor.Fonts.Count.ShouldBe(VideoFonts.All.Count);

        // The form and its preview follow the choices.
        editor.Name = "Shorts";
        editor.Palette = editor.Palettes.Single(p => p.Palette.Id == "paper");
        editor.TitleStyle = editor.TitleStyles.Single(c => c.Value == (int)TitleStyle.Boxed);
        editor.TitleCells.Single(c => c.Position == TitlePosition.BottomLeft).IsChecked = true;
        editor.ProgressBar = true;
        editor.Closing = editor.Placements.Single(c => c.Value == (int)ClosingPlacement.Both);
        editor.Platform = editor.Platforms.Single(c => c.Value == (int)ClosingPlatform.Instagram);
        editor.Phrase = editor.Phrases.Single(c => c.Value == (int)ClosingPhrase.MoreVideos);
        editor.Position.ShouldBe(TitlePosition.BottomLeft);
        editor.TitleCells.Count(c => c.IsChecked).ShouldBe(1);
        ((Avalonia.Media.ISolidColorBrush)editor.PreviewBackground).Color.ShouldBe(Avalonia.Media.Color.Parse("#F6F1E7"));
        ((Avalonia.Media.ISolidColorBrush)editor.PreviewTitleBox).Color.ShouldBe(Avalonia.Media.Color.Parse("#D9480F"), "a boxed title sits in the accent colour");
        editor.PreviewTitleVertical.ShouldBe(Avalonia.Layout.VerticalAlignment.Bottom);
        editor.PreviewTitleHorizontal.ShouldBe(Avalonia.Layout.HorizontalAlignment.Left);
        editor.ClosingOn.ShouldBeTrue();
        editor.Phrases.Single(c => c.Value == (int)ClosingPhrase.MoreVideos).Label.ShouldBe("Don't forget to follow for more videos!", "Instagram follows");
        editor.ClosingPreview.ShouldContain("Don't forget to follow for more videos!");

        // The platform changes the wording; the chosen sentence stays.
        editor.Platform = editor.Platforms.Single(c => c.Value == (int)ClosingPlatform.YouTube);
        editor.Phrase.ShouldNotBeNull().Label.ShouldBe("Don't forget to subscribe for more videos!");
        await section.SaveAsync();

        var stored = (await h.Get<IVideoStyleStore>().ListAsync(CancellationToken.None)).ShouldHaveSingleItem();
        (stored.Name, stored.PaletteId, stored.TitleStyle, stored.TitlePosition, stored.ProgressBar).ShouldBe(("Shorts", "paper", TitleStyle.Boxed, TitlePosition.BottomLeft, true));
        (stored.Closing, stored.ClosingPlatform, stored.ClosingPhrase).ShouldBe((ClosingPlacement.Both, ClosingPlatform.YouTube, ClosingPhrase.MoreVideos));
        section.Status.ShouldBe(text.Format("styles.saved", "Shorts"));
        section.SelectedStyle.ShouldNotBeNull().Id.ShouldBe(stored.Id);
        section.CanDelete.ShouldBeTrue();

        // Cancel puts the form back as stored.
        section.Editor!.Name = "Changed";
        section.Revert();
        section.Editor!.Name.ShouldBe("Shorts");

        // A wrong name is said and not stored.
        section.Editor.Name = " ";
        await section.SaveAsync();
        section.Status.ShouldBe(text["style.problem.namemissing"]);
        (await h.Get<IVideoStyleStore>().ListAsync(CancellationToken.None)).Single().Name.ShouldBe("Shorts");

        // Delete asks first; No keeps it, Yes removes it completely.
        confirm.Answer = false;
        await section.DeleteAsync();
        (await h.Get<IVideoStyleStore>().ListAsync(CancellationToken.None)).ShouldHaveSingleItem();
        confirm.Dangers.Last().ShouldBeTrue();
        confirm.Answer = true;
        await section.DeleteAsync();
        (await h.Get<IVideoStyleStore>().ListAsync(CancellationToken.None)).ShouldBeEmpty();
        section.Editor.ShouldBeNull();
        section.Empty.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task AtTheLimitNoTemplateIsAdded()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        for (var i = 0; i < VideoStyleLimits.Max; i++)
        {
            await h.Get<IVideoStyleStore>().SaveAsync(new VideoStyle { Name = "T" + i }, CancellationToken.None);
        }

        var section = (await h.Get<SettingsViewModel>().ShowAsync<VideoStylesSettingsViewModel>()).ShouldNotBeNull();
        section.CanAdd.ShouldBeFalse();
        section.CountText.ShouldBe(text.Format("styles.count", VideoStyleLimits.Max, VideoStyleLimits.Max));
        section.Add();
        section.Editor.ShouldBeNull();
        section.Status.ShouldBe(text.Format("styles.limit", VideoStyleLimits.Max));
    }

    [AvaloniaFact]
    public async Task TheTabShowsTheFormWithItsPreviewAndEveryChoiceHasALabelInBothLanguages()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var section = (await h.Get<SettingsViewModel>().ShowAsync<VideoStylesSettingsViewModel>()).ShouldNotBeNull();
        section.Add();
        var view = new TMConcept.Desktop.Views.VideoStylesSettingsView { DataContext = section };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1280, Height = 900 };
        h.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var names = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Avalonia.Controls.Control>()
            .Select(c => c.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[] { "StyleName", "StylePreview", "StylePalettes", "StyleFonts", "StyleTitleCells", "StyleClosing", "SaveStyleButton" })
        {
            names.ShouldContain(name);
        }

        window.Close();

        foreach (var language in Localizer.Supported)
        {
            var keys = Localizer.Keys(language).ToHashSet(StringComparer.Ordinal);
            var needed = VideoPalettes.All.Select(p => "style.palette." + p.Id)
                .Concat(Enum.GetNames<TitleStyle>().Select(n => "style.title." + n.ToLowerInvariant()))
                .Concat(Enum.GetNames<TitlePosition>().Select(n => "style.position." + n.ToLowerInvariant()))
                .Concat(Enum.GetNames<DrawStyle>().Select(n => "style.draw." + n.ToLowerInvariant()))
                .Concat(Enum.GetNames<MotionIntensity>().Select(n => "style.motion." + n.ToLowerInvariant()))
                .Concat(Enum.GetNames<ClosingPlacement>().Select(n => "style.closing." + n.ToLowerInvariant()))
                .Concat(Enum.GetNames<ClosingPlatform>().Select(n => "style.platform." + n.ToLowerInvariant()))
                .Concat(Enum.GetNames<VideoStyleProblem>().Select(n => "style.problem." + n.ToLowerInvariant()));
            needed.Where(k => !keys.Contains(k)).ShouldBeEmpty(language);
        }
    }
}

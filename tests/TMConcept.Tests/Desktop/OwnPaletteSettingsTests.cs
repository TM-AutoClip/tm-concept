using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.ViewModels;
using TMConcept.Storage.Stores;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.24 Settings → Templates: the user's own colours (typed, or made from one base colour), their contrast check in the form, saving them as
/// a named palette any template can pick, and deleting it (templates keep their copy); readable text and the font emphasis are saved too.
/// </summary>
public sealed class OwnPaletteSettingsTests
{
    [Fact]
    public async Task SavedPalettesAreKeptInTheDatabaseWithinTheirLimit()
    {
        using var h = await StorageHarness.CreatedAsync();
        var store = new VideoPaletteStore(h.Database);
        var first = new SavedPalette { Name = "  Sea  ", Colors = PaletteDesigner.FromBase("#2E86DE", false) };
        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(new SavedPalette { Name = "Autumn", Colors = PaletteDesigner.FromBase("#D9480F", true) }, CancellationToken.None);

        var all = await store.ListAsync(CancellationToken.None);
        all.Select(p => p.Name).ShouldBe(["Autumn", "Sea"]);
        all.Single(p => p.Name == "Sea").Colors.ShouldBe(first.Colors);

        first.Name = "Deep sea";
        await store.SaveAsync(first, CancellationToken.None);
        (await store.ListAsync(CancellationToken.None)).Select(p => p.Name).ShouldBe(["Autumn", "Deep sea"], "same id: replaced, not added");

        await Should.ThrowAsync<ArgumentException>(() => store.SaveAsync(new SavedPalette { Name = "Grey",
            Colors = PaletteDesigner.Own("#777777", "#7A7A7A", "#7B7B7B", "#FFFFFF", "#888888", "#808080") }, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => store.SaveAsync(new SavedPalette { Name = " ", Colors = first.Colors }, CancellationToken.None));

        for (var i = 2; i < VideoStyleLimits.PalettesMax; i++)
        {
            await store.SaveAsync(new SavedPalette { Name = "P" + i, Colors = first.Colors }, CancellationToken.None);
        }

        await Should.ThrowAsync<VideoStyleLimitException>(() => store.SaveAsync(new SavedPalette { Name = "One too many", Colors = first.Colors },
            CancellationToken.None));
        await store.DeleteAsync(first.Id, CancellationToken.None);
        await store.DeleteAsync(Guid.NewGuid(), CancellationToken.None);
        (await store.ListAsync(CancellationToken.None)).Count.ShouldBe(VideoStyleLimits.PalettesMax - 1);
    }

    [AvaloniaFact]
    public async Task OwnColoursAreMadeCheckedSavedAsAPaletteAndUsedByTheTemplate()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var section = (await h.Get<SettingsViewModel>().ShowAsync<VideoStylesSettingsViewModel>()).ShouldNotBeNull();
        section.Add();
        var editor = section.Editor.ShouldNotBeNull();
        editor.OwnColours.ShouldBeFalse("a shipped palette is chosen at first");
        editor.Palettes[^1].Own.ShouldBeTrue();
        editor.Palettes[^1].Label.ShouldBe(text["styles.own"]);

        // Typed colours that cannot be read: said in the form, the template cannot be saved.
        editor.Palette = editor.Palettes[^1];
        editor.OwnColours.ShouldBeTrue();
        editor.EditText = "#16244A";
        editor.PaletteOk.ShouldBeFalse();
        editor.PaletteStatus.ShouldContain(text["palette.issue.textonbackground"]);
        await section.SaveAsync();
        section.Status.ShouldBe(text["style.problem.palettecontrast"]);

        // A palette from one base colour passes and shows in the preview.
        editor.BaseColour = "#E5383B";
        editor.GenerateLight = true;
        editor.Generate();
        editor.PaletteOk.ShouldBeTrue();
        var made = PaletteDesigner.FromBase("#E5383B", light: true);
        editor.EditBackground.ShouldBe(made.Background);
        ((Avalonia.Media.ISolidColorBrush)editor.PreviewBackground).Color.ShouldBe(Avalonia.Media.Color.Parse(made.Background));
        editor.ReadableText.ShouldBeTrue("readable text is on for a new template");
        editor.Emphasis = editor.Emphases.Single(c => c.Value == (int)FontEmphasis.BoldItalic);
        editor.Font = editor.Fonts.Single(f => f.Font.Id == "tinos");

        // Saved as a named palette: it is listed and chosen; the template keeps a copy of the colours.
        editor.PaletteName = "Brick";
        await section.SavePaletteAsync();
        section.Status.ShouldBe(text.Format("styles.own.saved", "Brick"));
        editor.SavedChosen.ShouldBeTrue();
        editor.Palette.Label.ShouldBe("Brick");
        await section.SaveAsync();
        var stored = (await h.Get<IVideoStyleStore>().ListAsync(CancellationToken.None)).ShouldHaveSingleItem();
        (stored.PaletteId, stored.CustomPalette, stored.CustomPaletteName).ShouldBe((VideoPalettes.CustomId, made, "Brick"));
        (stored.FontId, stored.FontEmphasis, stored.ReadableText).ShouldBe(("tinos", FontEmphasis.BoldItalic, true));

        // Opened again: the saved palette is chosen; deleting it keeps the template's copy.
        section.Revert();
        var again = section.Editor.ShouldNotBeNull();
        again.Palette.Label.ShouldBe("Brick");
        again.Changed.ShouldBeFalse();
        await section.DeletePaletteAsync();
        confirm.Asked.ShouldNotBeEmpty();
        section.Status.ShouldBe(text.Format("styles.own.deleted", "Brick"));
        (await h.Get<IVideoPaletteStore>().ListAsync(CancellationToken.None)).ShouldBeEmpty();
        again.Palette.Own.ShouldBeTrue("the colours stay as the template's own");
        again.EditBackground.ShouldBe(made.Background);
        VideoStyleRules.Palette((await h.Get<IVideoStyleStore>().ListAsync(CancellationToken.None)).Single()).ShouldBe(made);
    }
}

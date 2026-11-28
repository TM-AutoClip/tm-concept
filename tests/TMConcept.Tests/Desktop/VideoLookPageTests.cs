using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Styles;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 28 Nov 2026 (the developer: "şablondan kaldıralım dedim … bu video üretim seçimi gibi bir şey bunu video kodu ile ayarlamalı"): the drawing
/// style, the motion and the finishing layer are chosen on the video page next to the video code model, per profile, "Yapay zekâ seçsin" by
/// default; the template no longer holds them.
/// </summary>
public sealed class VideoLookPageTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [AvaloniaFact]
    public void TheLookStartsOnTheAiChoiceAndEveryStyleIsDescribed()
    {
        using var h = new DesktopHarness();
        var text = h.Get<Localizer>();
        var look = h.Get<VideoLookViewModel>();

        look.DrawStyles[0].Value.ShouldBe((int)DrawStyle.Auto);
        look.DrawStyles.Select(c => c.Value).ShouldBe(DrawStyles.All.Select(d => (int)d.Style), "the catalog's order, the AI choice first");
        look.DrawStyle!.Value.ShouldBe((int)DrawStyle.Auto);
        (look.Motion!.Value, look.Finish).ShouldBe(((int)MotionIntensity.Normal, true));
        look.DrawStyleNote.ShouldBe(text["style.draw.auto.desc"]);

        look.DrawStyle = look.DrawStyles.Single(c => c.Value == (int)DrawStyle.Cartoon);
        look.DrawStyleNote.ShouldEndWith(text["styles.draw.prefers_images"], customMessage: "a style that needs an image model says so");
        look.ToLook().DrawStyle.ShouldBe(DrawStyle.Cartoon);

        // A list that writes an empty selection back (the page torn down) neither throws nor saves.
        var saved = 0;
        look.Changed += (_, _) => saved++;
        look.DrawStyle = null;
        look.Motion = null;
        saved.ShouldBe(0, "an empty selection is not a choice");
        (look.ToLook().DrawStyle, look.ToLook().Motion).ShouldBe((DrawStyle.Auto, MotionIntensity.Normal), "an empty selection reads as the default look");
    }

    [AvaloniaFact]
    public void ThePaletteSuggestionAndTheDrawerNoteFollowTheChosenTemplate()
    {
        using var h = new DesktopHarness();
        var text = h.Get<Localizer>();
        var look = h.Get<VideoLookViewModel>();
        look.DrawStyle = look.DrawStyles.Single(c => c.Value == (int)DrawStyle.Blueprint);
        look.SuggestedPaletteText.ShouldBeEmpty("no template chosen yet");

        // The palette is the template's (the channel's colours): the suggestion is only information, with the template's palette beside it.
        look.ShowTemplate(new VideoStyle { Name = "t", PaletteId = "paper", Drawer = SceneDrawer.App });
        look.SuggestedPaletteText.ShouldBe(text.Format("look.palette", text["style.palette.blueprint"], text["style.palette.paper"]));
        look.FinishNote.ShouldBeEmpty();
        look.ShowTemplate(new VideoStyle { Name = "t", PaletteId = "blueprint", Drawer = SceneDrawer.App });
        look.SuggestedPaletteText.ShouldBeEmpty("the template already has it");

        // The finishing layer works only when the app draws.
        look.ShowTemplate(new VideoStyle { Name = "t", Drawer = SceneDrawer.FreeCode });
        look.FinishNote.ShouldBe(text["look.finish.drawer_note"]);
        look.Finish = false;
        look.FinishNote.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task TheLookIsSavedPerProfileAndTwoProfilesKeepTheirOwn()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var one = new Profile { Name = "One", ContentLanguage = ContentLanguages.English };
        var two = new Profile { Name = "Two", ContentLanguage = ContentLanguages.English };
        await h.Get<IProfileStore>().SaveAsync(one, None);
        await h.Get<IProfileStore>().SaveAsync(two, None);
        await h.Get<AppSession>().SelectProfileAsync(one, None);
        var store = h.Get<IVideoLookStore>();
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        page.Look.DrawStyle!.Value.ShouldBe((int)DrawStyle.Auto, "a profile without a choice: the AI chooses");

        page.Look.DrawStyle = page.Look.DrawStyles.Single(c => c.Value == (int)DrawStyle.StickFigure);
        page.Look.Motion = page.Look.Motions.Single(c => c.Value == (int)MotionIntensity.Lively);
        page.Look.Finish = false;
        await Until(async () => (await store.GetAsync(one.Id, None)) is { DrawStyle: DrawStyle.StickFigure, Motion: MotionIntensity.Lively, Finish: false });
        (await store.GetAsync(two.Id, None)).DrawStyle.ShouldBe(DrawStyle.Auto, "another profile has its own (default) look");

        await h.Get<AppSession>().SelectProfileAsync(two, None);
        await page.ProfileChangedAsync();
        page.Look.DrawStyle!.Value.ShouldBe((int)DrawStyle.Auto);
        page.Look.Finish.ShouldBeTrue();
        (await store.GetAsync(one.Id, None)).DrawStyle.ShouldBe(DrawStyle.StickFigure, "loading another profile saves nothing");

        await h.Get<AppSession>().SelectProfileAsync(one, None);
        await page.ProfileChangedAsync();
        (page.Look.DrawStyle!.Value, page.Look.Motion!.Value, page.Look.Finish).ShouldBe(((int)DrawStyle.StickFigure, (int)MotionIntensity.Lively, false));
    }

    private static async Task Until(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 150 && !await condition(); i++)
        {
            await Task.Delay(20);
        }

        (await condition()).ShouldBeTrue();
    }
}

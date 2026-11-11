using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop.Screenshots;

/// <summary>
/// Screenshots of every page at 1280×720 (the window's opening size) and 1920×1080 (the shell also at its minimum, 1024×600), for a person
/// to look at; nothing is asserted about pixels. Off unless <c>TMCONCEPT_SCREENSHOTS</c> names the output folder; Turkish unless <c>TMCONCEPT_SCREENSHOTS_LANG=en</c>. Files:
/// <c>&lt;folder&gt;/&lt;page&gt;-&lt;w&gt;x&lt;h&gt;-&lt;lang&gt;.png</c>. Headless Skia on the processor; no provider, no ffmpeg.
/// </summary>
public sealed class ScreenshotTests
{
    private static readonly (int Width, int Height)[] Sizes = [(1280, 720), (1920, 1080)];

    private static string Folder => Environment.GetEnvironmentVariable("TMCONCEPT_SCREENSHOTS") ?? string.Empty;

    private static string Language => Environment.GetEnvironmentVariable("TMCONCEPT_SCREENSHOTS_LANG") is "en" ? "en" : "tr";

    [AvaloniaFact]
    public Task ShellWithTheLogOpen() => ShootAsync("shell-log-open", scene =>
    {
        scene.Shell.LogOpen = true;
        return scene.Shell.NavigateAsync<ConceptsViewModel>();
    });

    [AvaloniaFact]
    public Task ShellAtTheMinimumSize() => ShootAsync("shell-min", scene => scene.Shell.NavigateAsync<ConceptsViewModel>(), [(1024, 600)]);

    [AvaloniaFact]
    public Task Concepts() => ShootAsync("concepts", async scene =>
    {
        var page = await ConceptsAsync(scene);
        await page.ChooseAsync(page.NewConcepts[0]);
    }, [(1280, 720), (1920, 1080), (1024, 600)]);

    [AvaloniaFact]
    public Task ConceptsEditing() => ShootAsync("concepts-editing", async scene =>
    {
        var page = await ConceptsAsync(scene);
        await page.ChooseAsync(page.NewConcepts[1]);
        await page.Detail.EditAsync();
    });

    [AvaloniaFact]
    public Task ConceptsApproved() => ShootAsync("concepts-approved", async scene =>
    {
        var page = await ConceptsAsync(scene);
        page.ShowApprovedList();
        await page.ChooseAsync(page.Approved[0]);
    });

    [AvaloniaFact]
    public Task Video() => ShootAsync("video", VideoAsync, [(1280, 720), (1920, 1080), (1100, 720)]);

    [AvaloniaFact]
    public Task Produced() => ShootAsync("produced", async scene =>
    {
        var page = await scene.Shell.NavigateAsync<ProducedViewModel>();
        page!.Selected = page.Items.FirstOrDefault();
    });

    [AvaloniaFact]
    public Task Trash() => ShootAsync("trash", scene => scene.Shell.NavigateAsync<TrashViewModel>());

    [AvaloniaFact]
    public Task Profile() => ShootAsync("profile", scene => scene.Shell.NavigateAsync<ProfileViewModel>());

    [AvaloniaFact]
    public Task Settings() => ShootAsync("settings", scene => scene.Shell.NavigateAsync<SettingsViewModel>());

    /// <summary>
    /// The video page as it is used: three concepts queued, the first one being made (narration done, images done, the code model writing,
    /// what was paid so far), an approved concept open in the detail panel, the made video chosen with its poster in the preview.
    /// </summary>
    private static async Task VideoAsync(ScreenshotScene scene)
    {
        var turkish = scene.Language == "tr";
        var titles = turkish
            ? new[] { "Haliç'i kapatan zincir", "Gemiler karadan nasıl yürüdü?", "Surlarda son nöbet", "Ayasofya'da son ayin" }
            : new[] { "The chain across the Golden Horn", "How the ships crossed the land", "The last watch on the walls", "The last mass in Hagia Sophia" };
        var content = turkish
            ? "[ekranda: Haliç'in girişi]\nŞehrin limanı kalın bir zincirle kapatılmıştı.\n[ekranda: yağlanmış kızaklar]\nOsmanlı gemileri bir gecede tepeyi aştı."
            : "[on screen: the mouth of the Golden Horn]\nA thick chain closed the harbour.\n[on screen: greased rollers]\nIn one night the ships crossed the hill.";
        await scene.Harness.Get<TMConcept.Core.Concepts.IConceptStore>().AddAsync(titles.Select(t => new TMConcept.Core.Concepts.Concept
        {
            ProfileId = scene.Profile.Id, Title = t, Summary = turkish ? "Kuşatmanın en cesur hamlesi." : "The boldest move of the siege.", Content = content,
            Language = scene.Profile.ContentLanguage, Status = TMConcept.Core.Concepts.ConceptStatus.Approved,
        }).ToList(), CancellationToken.None);
        var page = (await scene.Shell.NavigateAsync<VideoViewModel>())!;
        foreach (var (step, model) in new[] { (TMConcept.Core.Pipeline.PipelineSteps.VideoCode, "claude-sonnet-5-5"), (TMConcept.Core.Pipeline.PipelineSteps.Concepts, "claude-sonnet-5-5") })
        {
            var choice = page.Picker.Row(step)!;
            choice.Selected = choice.Options.First(o => o.Model?.Id == model);
        }

        foreach (var title in titles.Take(3))
        {
            await page.EnqueueAsync(page.Approved.First(c => c.Title.EndsWith(title, StringComparison.Ordinal)));
        }

        var row = page.Queue[0];
        var progress = scene.Harness.Get<Func<ProductionProgressViewModel>>()();
        row.Progress = progress;
        row.Busy = true;
        progress.Begin("work.video", "Claude Sonnet 5.5");
        IReadOnlyList<string> plan = [TMConcept.Core.Videos.VideoSteps.Prepare, TMConcept.Core.Videos.VideoSteps.Speech, TMConcept.Core.Videos.VideoSteps.Images,
            TMConcept.Core.Videos.VideoSteps.Timing, TMConcept.Core.Videos.VideoSteps.Code, TMConcept.Core.Videos.VideoSteps.Render,
            TMConcept.Core.Videos.VideoSteps.Mux, TMConcept.Core.Videos.VideoSteps.Save];
        progress.Report(new TMConcept.Core.Videos.VideoProgress(0.02, TMConcept.Core.Videos.VideoSteps.Speech, string.Empty, 0, plan, 0, 8));
        progress.Report(new TMConcept.Core.Videos.VideoProgress(0.2, TMConcept.Core.Videos.VideoSteps.Speech, string.Empty, 0.012m, plan, 8, 8));
        progress.Report(new TMConcept.Core.Videos.VideoProgress(0.2, TMConcept.Core.Videos.VideoSteps.Images, string.Empty, 0.012m, plan, 0, 8));
        progress.Report(new TMConcept.Core.Videos.VideoProgress(0.3, TMConcept.Core.Videos.VideoSteps.Images, string.Empty, 0.092m, plan, 8, 8));
        progress.Report(new TMConcept.Core.Videos.VideoProgress(0.37, TMConcept.Core.Videos.VideoSteps.Code, string.Empty, 0.102m, plan, 0, 0,
            new TMConcept.Core.Chat.ChatProgress(TMConcept.Core.Chat.ChatPhase.Writing, TimeSpan.FromSeconds(154), 12_400, string.Empty)));
        await page.ChooseAsync(page.Approved[0]);
        page.SelectedProduced = page.Produced.FirstOrDefault();
    }

    /// <summary>
    /// The concept page as it is used: models chosen, 12 suggested topics (four ticked) from the fake chat, an idea typed in section B.
    /// </summary>
    private static async Task<ConceptsViewModel> ConceptsAsync(ScreenshotScene scene)
    {
        var page = (await scene.Shell.NavigateAsync<ConceptsViewModel>())!;
        var concepts = page.Settings.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!;
        concepts.Selected = concepts.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
        var topics = page.Settings.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Topics)!;
        topics.Selected = topics.Options.First(o => o.Model?.Id == "claude-haiku-5-5");
        page.Settings.ModelsOpen = false;
        var turkish = scene.Language == "tr";
        var suggested = turkish
            ? new[]
            {
                "Konstantinopolis'in zincirle kapatılan limanı", "Bizans sarayında hadımların gücü", "Basileios'un Bulgar esirleri",
                "Varangian muhafızlarının kuzeyden gelişi", "Manzikert'te kaybedilen ordu", "Bizans'ta ikonaların kırıldığı yıllar",
                "Haçlıların 1204'te şehri yağmalaması", "Theodora: tiyatrodan imparatoriçeliğe", "Bizans'ın altın sikkesi solidus",
                "Hipodromdaki Mavi ve Yeşil takımlar", "Trabzon İmparatorluğu'nun son günü", "Kariye'nin mozaikleri neyi anlatır?",
            }
            : new[]
            {
                "The chain that closed the Golden Horn", "Eunuchs at the Byzantine court", "Basil II and the Bulgarian prisoners",
                "The Varangian Guard arrives from the north", "The army lost at Manzikert", "The years the icons were broken",
                "The sack of Constantinople in 1204", "Theodora: from the stage to the throne", "The solidus, Byzantium's gold coin",
                "Blues and Greens in the Hippodrome", "The last day of the Empire of Trebizond", "What the mosaics of the Chora tell",
            };
        scene.Chat.Answer(_ => new TMConcept.Core.Chat.ChatResponse(
            "{\"scope\": \"s\", \"topics\": [" + string.Join(", ", suggested.Select(t => System.Text.Json.JsonSerializer.Serialize(t))) + "]}",
            new TMConcept.Core.Chat.ChatUsage(600, 0, 0, 240), null, "end_turn", "r"));
        page.Topics.TopicCount = 12;
        await page.Topics.SuggestAsync();
        foreach (var i in new[] { 0, 2, 3, 6 })
        {
            page.Topics.Topics[i].Checked = true;
        }

        page.Ideas.Idea = turkish
            ? "Bizans'ın son günleri; kuşatmayı surlardaki bir askerin gözünden anlat"
            : "The last days of Byzantium, told by a soldier on the walls";
        page.Ideas.Count = 5;
        return page;
    }

    private static async Task ShootAsync(string page, Func<ScreenshotScene, Task> arrange, (int Width, int Height)[]? sizes = null)
    {
        Assert.SkipUnless(Folder.Length > 0, "Screenshots are taken only when TMCONCEPT_SCREENSHOTS names an output folder.");
        foreach (var (width, height) in sizes ?? Sizes)
        {
            // A new scene per size: the window opens at its size (as the app does) instead of being resized after it was shown.
            using var scene = await ScreenshotScene.CreateAsync(Language);
            var window = scene.Open(width, height);
            await arrange(scene);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            ScreenshotWriter.Save(window, Path.Combine(Folder, $"{page}-{width}x{height}-{scene.Language}.png"));
            window.Close();
        }
    }
}

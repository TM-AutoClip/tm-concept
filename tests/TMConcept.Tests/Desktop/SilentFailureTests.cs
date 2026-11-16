using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// A press that cannot run says why on its own (red) line and in the activity panel (0.1.15: "Suggest topics" without a profile did nothing).
/// Without a profile the buttons stay on, so the press can say it, and the profile page is asked for.
/// </summary>
public sealed class SilentFailureTests
{
    [AvaloniaFact]
    public async Task SuggestTopicsWithoutAProfileSaysSoInRedAndAsksForAProfile()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var asked = 0;
        h.Get<AppSession>().ProfileWanted += (_, _) => asked++;
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();

        page.Topics.CanSuggest.ShouldBeTrue("the button stays on so that a press can say why");
        await page.Topics.SuggestAsync();

        page.Topics.SuggestFailed.ShouldBeTrue();
        page.Topics.SuggestStatus.ShouldBe(text["profile.needed"]);
        h.Get<IActivityFeed>().Recent()[^1].Text.ShouldBe(text["profile.needed"]);
        asked.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task ProducingAnEmptyQueueOrQueueingWithoutAProfileSaysWhy()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();

        // No profile.
        await page.ProduceAllAsync();
        page.Status.ShouldBe(text["profile.needed"]);
        await page.EnqueueAsync(new ConceptCardViewModel(new TMConcept.Core.Concepts.Concept { Title = "x" }, text));
        h.Get<IActivityFeed>().Recent()[^1].Text.ShouldBe(text["profile.needed"]);

        // A profile with an empty queue.
        var profile = new TMConcept.Core.Profiles.Profile { Name = "Byzantium", ContentLanguage = "en", ConceptText = "x" };
        await h.Get<TMConcept.Core.Profiles.IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await page.ActivateAsync();
        await page.ProduceAllAsync();

        page.Status.ShouldBe(text["video.queue.empty"]);
        var last = h.Get<IActivityFeed>().Recent()[^1];
        (last.Text, last.IsWarning).ShouldBe((text["video.queue.empty"], true));
    }
}

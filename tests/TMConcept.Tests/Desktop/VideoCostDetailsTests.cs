using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Paths;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.24: the cost of a produced video in detail, with its share of the concept generation explained ("10 concepts cost 0.20 USD → this
/// one 0.02 USD"), worked out from the generation run when the video has no stored share, counted once for later versions, and said to be
/// unknown (never guessed) when neither the share nor the run is there any more.
/// </summary>
public sealed class VideoCostDetailsTests
{
    private static VideoCost Cost(Guid video, string step, decimal usd) => new() { VideoId = video, Step = step, Usd = usd, Exact = true };

    [Fact]
    public void TheShareComesFromTheStoredLineTheRunOrIsUnknown()
    {
        var run = new Run { Kind = RunKind.Concepts, ActualUsd = 0.20m, Accepted = 10, CostExact = true };
        var concept = new Concept { RunId = run.Id };
        var video = Guid.NewGuid();

        var stored = VideoCostBreakdown.Share([Cost(video, PipelineSteps.Concepts, 0.02m)], concept, run, firstVersion: true);
        (stored.Kind, stored.Usd, stored.RunUsd, stored.Kept, stored.Known).ShouldBe((ConceptShareKind.Recorded, 0.02m, 0.20m, 10, true));

        var fromRun = VideoCostBreakdown.Share([Cost(video, PipelineSteps.VideoCode, 0.1m)], concept, run, firstVersion: true);
        (fromRun.Kind, fromRun.Usd, fromRun.Exact).ShouldBe((ConceptShareKind.FromRun, 0.02m, true));

        var free = VideoCostBreakdown.Share([], concept, new Run { Id = run.Id, ActualUsd = 0, Accepted = 4 }, firstVersion: true);
        (free.Kind, free.Usd).ShouldBe((ConceptShareKind.FromRun, 0m), "a local model's run costs nothing and stores no line: free, not unknown");

        VideoCostBreakdown.Share([], new Concept(), null, firstVersion: true).Kind.ShouldBe(ConceptShareKind.Handwritten);
        VideoCostBreakdown.Share([], concept, run, firstVersion: false).Kind.ShouldBe(ConceptShareKind.FirstVersion);
        VideoCostBreakdown.Share([], null, null, firstVersion: true).Kind.ShouldBe(ConceptShareKind.Unknown, "the concept was deleted");
        VideoCostBreakdown.Share([], concept, null, firstVersion: true).Kind.ShouldBe(ConceptShareKind.Unknown, "the run is gone");
        VideoCostBreakdown.Share([], concept, new Run(), firstVersion: true).Kind.ShouldBe(ConceptShareKind.Unknown, "another run is not this concept's");
    }

    [AvaloniaFact]
    public async Task TheDetailsExplainTheShareAndSayWhenItIsUnknown()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var run = new Run { ProfileId = profile.Id, Kind = RunKind.Concepts, ActualUsd = 0.20m, Accepted = 10, CostExact = true, Status = RunStatus.Succeeded };
        await h.Get<IRunStore>().AddAsync(run, CancellationToken.None);
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = "c", Language = "en", RunId = run.Id,
            Status = ConceptStatus.Produced };
        await h.Get<IConceptStore>().AddAsync([concept], CancellationToken.None);
        var library = h.Get<IAppPaths>().LibraryDirectory;

        // A video made before the share was stored for free runs: worked out from the run.
        var known = new VideoRecord { ConceptId = concept.Id, ProfileId = profile.Id, Title = "Greek fire", Folder = "Byzantium/a/v1", FileName = "video.mp4",
            TotalUsd = 0.12m };
        await h.Get<IVideoStore>().AddAsync(known, [Cost(known.Id, PipelineSteps.VideoCode, 0.12m)], CancellationToken.None);

        // A video whose concept is gone and that has no stored share: unknown.
        var orphan = new VideoRecord { ProfileId = profile.Id, Title = "Lost", Folder = "Byzantium/b/v1", FileName = "video.mp4", TotalUsd = 0.05m,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5) };
        await h.Get<IVideoStore>().AddAsync(orphan, [Cost(orphan.Id, PipelineSteps.Speech, 0.05m)], CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(library, "Byzantium"));

        var page = h.Get<ProducedViewModel>();
        await page.ActivateAsync();

        async Task<string> DetailsOf(Guid id)
        {
            page.Selected = page.Items.Single(r => r.Video.Id == id);
            for (var i = 0; i < 100 && !page.Details.Contains(text["produced.cost.concepts"], StringComparison.Ordinal); i++)
            {
                await Task.Delay(20);
            }

            return page.Details;
        }

        var first = await DetailsOf(known.Id);
        first.ShouldContain(text["produced.cost.concepts"] + ": " + text.Format("produced.share.amount", text.Usd(0.02m), text["produced.exact"]));
        first.ShouldContain(text.Format("produced.share.math", 10, text.Usd(0.20m), text.Usd(0.02m)));
        first.ShouldContain(text.Format("produced.detail.total_share", text.Usd(0.14m), text.Usd(0.02m), text.Usd(0.12m)));

        var second = await DetailsOf(orphan.Id);
        second.ShouldContain(text["produced.cost.concepts"] + ": " + text["produced.share.unknown"]);
        second.ShouldContain(text.Format("produced.detail.total_unknown", text.Usd(0.05m)));
        second.ShouldContain(text["produced.cost.speech"]);
    }
}

using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Work;

namespace TMConcept.Tests.Desktop;

public sealed class SpendingMeterTests
{
    [AvaloniaFact]
    public async Task LiveSpendingOfTwoWorksIsSummedAndAnEndedWorkLeavesTheSum()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var meter = h.Get<SpendingMeter>();
        var changes = 0;
        meter.Changed += (_, _) => changes++;
        object concepts = new(), video = new();

        meter.Live(concepts, 0.01m);
        meter.Live(video, 0.20m);
        meter.Live(concepts, 0.03m);
        meter.LiveUsd.ShouldBe(0.23m, "one value per work, replaced, then summed");
        changes.ShouldBe(3);
        meter.Live(concepts, 0.03m);
        changes.ShouldBe(3, "the same value is not a change");

        await meter.EndAsync(concepts);
        meter.LiveUsd.ShouldBe(0.20m);
        meter.MonthUsd.ShouldBe(0, "the month was read again and nothing is saved");
        await meter.EndAsync(video);
        meter.LiveUsd.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task TheMonthCountsTheRunsAndTheVideosWithTheirParts()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        var text = h.Get<Localizer>();
        var profile = new Profile { Name = "Byzantium", ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<IRunStore>().AddAsync(new Run { ProfileId = profile.Id, Kind = RunKind.Concepts, Provider = "anthropic", ActualUsd = 0.12m,
            Status = RunStatus.Succeeded }, CancellationToken.None);
        await h.Get<IVideoStore>().AddAsync(new VideoRecord { ProfileId = profile.Id, Title = "Greek fire", TotalUsd = 0.30m }, [],
            CancellationToken.None);

        var meter = h.Get<SpendingMeter>();
        await meter.RefreshAsync();

        meter.MonthUsd.ShouldBe(0.42m);
        meter.Breakdown.ShouldContain("anthropic " + text.Usd(0.12m));
        meter.Breakdown.ShouldContain(text.Format("models.spending.videos", text.Usd(0.30m)));
    }
}

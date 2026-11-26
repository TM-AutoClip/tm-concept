using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Updates;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.55: the start offers a newer version BEFORE the components question of a version change (0.1.54 asked first, so the components of the
/// old version were installed before the new one was seen). The start waits for the look a few seconds at most: an answer within the wait is
/// offered at once (installed = the start asks nothing more), a later answer is offered after the question, a failed look is only logged.
/// A fake clock and a fake window: nothing reaches the network.
/// </summary>
public sealed class StartUpdateOfferTests
{
    private static readonly UpdateOffer Offer = new(new AppVersion(0, 1, 55, string.Empty), "0.1.54", "## English\n- New.", UpdateRules.ReleasesPage, null, null,
        string.Empty);

    [Theory]
    [InlineData(true, StartUpdateOutcome.Installed)]
    [InlineData(false, StartUpdateOutcome.Later)]
    public async Task AnAnswerWithinTheWaitIsOfferedBeforeTheStartGoesOn(bool install, StartUpdateOutcome expected)
    {
        var dialog = new FakeDialog { Install = install };
        var offers = new StartUpdateOffer(dialog, new FakeTimeProvider(), NullLogger<StartUpdateOffer>.Instance);

        var outcome = await offers.OfferFirstAsync(Task.FromResult<UpdateOffer?>(Offer), UpdateRules.StartWait);

        outcome.ShouldBe(expected);
        dialog.Shown.ShouldBe([Offer]);
    }

    [Fact]
    public async Task ALookSlowerThanTheWaitDoesNotHoldTheStartAndIsOfferedWhenItAnswers()
    {
        var dialog = new FakeDialog();
        var clock = new FakeTimeProvider();
        var offers = new StartUpdateOffer(dialog, clock, NullLogger<StartUpdateOffer>.Instance);
        var look = new TaskCompletionSource<UpdateOffer?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = offers.OfferFirstAsync(look.Task, UpdateRules.StartWait);
        first.IsCompleted.ShouldBeFalse("the start waits for the look");
        clock.Advance(UpdateRules.StartWait);

        (await first).ShouldBe(StartUpdateOutcome.Pending);
        dialog.Shown.ShouldBeEmpty("nothing to offer yet: the components question comes first");

        var later = offers.OfferAsync(look.Task);
        look.SetResult(Offer);

        (await later).ShouldBe(StartUpdateOutcome.Later);
        dialog.Shown.ShouldBe([Offer]);
    }

    [Fact]
    public async Task NoNewerVersionOrAFailedLookLetsTheStartGoOnWithoutAWindow()
    {
        var dialog = new FakeDialog();
        var offers = new StartUpdateOffer(dialog, new FakeTimeProvider(), NullLogger<StartUpdateOffer>.Instance);

        (await offers.OfferFirstAsync(Task.FromResult<UpdateOffer?>(null), UpdateRules.StartWait)).ShouldBe(StartUpdateOutcome.None);
        (await offers.OfferFirstAsync(Task.FromException<UpdateOffer?>(new HttpRequestException("no internet")), UpdateRules.StartWait))
            .ShouldBe(StartUpdateOutcome.None);
        (await offers.OfferFirstAsync(Task.FromException<UpdateOffer?>(new TaskCanceledException("timeout")), UpdateRules.StartWait))
            .ShouldBe(StartUpdateOutcome.None);

        dialog.Shown.ShouldBeEmpty();
        UpdateRules.StartWait.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(5), "no internet never holds the start up for long");
    }

    private sealed class FakeDialog : IAppUpdateDialog
    {
        public bool Install { get; init; }

        public List<UpdateOffer> Shown { get; } = [];

        public Task<bool> ShowAsync(UpdateOffer offer)
        {
            Shown.Add(offer);
            return Task.FromResult(Install);
        }
    }
}

using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Keys;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

public sealed class ConceptPageTests
{
    private const string Secret = "fake-test-key-0123456789abcdef";

    private static async Task<Profile> PrepareAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = Secret }, CancellationToken.None);
        return profile;
    }

    private static async Task<ConceptsViewModel> PageAsync(DesktopHarness h)
    {
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        page.SelectedModel = page.Models.First(m => m.Model?.Id == "claude-sonnet-5-5");
        return page;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task GenerateInBatchesThrowsAwayDuplicatesAndOffersToFillTheMissing()
    {
        var first = FakeChat.Distinct(20);
        var fake = new FakeChat()
            .Concepts(first)
            .Concepts([.. FakeChat.Distinct(4), first[2]]);
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Count = 25;
        page.Estimate.ShouldContain("Claude Sonnet 5.5");

        await page.GenerateAsync();

        fake.Requests.Count.ShouldBe(2);
        fake.KeysUsed.ShouldAllBe(k => k == Secret);
        fake.Requests[0].CacheSystem.ShouldBeTrue();
        fake.Requests[1].System.ShouldContain("- " + first[19]);
        page.NewConcepts.Count.ShouldBe(24);
        page.Missing.ShouldBe(1);
        page.LastCost.ShouldNotBeEmpty();
        var numbers = (await h.Get<IConceptStore>().ListAsync(profile.Id, [ConceptStatus.New], false, CancellationToken.None)).Select(c => c.Number).ToList();
        numbers.ShouldBe(Enumerable.Range(1, 24).ToList(), ignoreOrder: true);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.Accepted.ShouldBe(24);
        run.RejectedDuplicates.ShouldBe(1);
        run.ActualUsd.ShouldBe(2 * ((1000 * 2.00m) + (2000 * 10.00m)) / 1_000_000m);
    }

    [AvaloniaFact]
    public async Task AnErrorKeepsTheFinishedBatchesAndTheBilledCost()
    {
        var fake = new FakeChat()
            .Concepts(FakeChat.Distinct(20))
            .Answer(_ => throw new ProviderException("cut") { Code = "truncated", ChargedUsage = new ChatUsage(1000, 0, 0, 1000) });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Count = 40;

        await page.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(20);
        page.Missing.ShouldBe(20);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldBe("truncated");
        run.OutputTokens.ShouldBe(3000);
    }

    [AvaloniaFact]
    public async Task ApproveTrashRestoreAndDeletePermanently()
    {
        var fake = new FakeChat().Concepts(["Walls of Theodosius", "Greek fire", "Iconoclasm explained"]);
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IChatClientFactory>(fake);
            s.AddSingleton<IConfirmDialog>(confirm);
        });
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Count = 3;
        await page.GenerateAsync();
        var trash = h.Get<TrashViewModel>();
        await trash.ActivateAsync();

        var walls = page.NewConcepts.First(c => c.Title.Contains("Walls", StringComparison.Ordinal));
        page.Selected = walls;
        await Until(() => walls.Lines.Count > 0);
        walls.Lines.ShouldContain(l => l.IsScene);
        await page.ApproveAsync(walls);
        page.Approved.Count.ShouldBe(1);
        var greek = page.NewConcepts.First(c => c.Title.Contains("Greek fire", StringComparison.Ordinal));
        var iconoclasm = page.NewConcepts.First(c => c.Title.Contains("Iconoclasm", StringComparison.Ordinal));
        await page.TrashAsync(greek);
        await page.TrashAsync(iconoclasm);
        page.NewConcepts.ShouldBeEmpty();
        await Until(() => trash.Items.Count == 2);
        trash.Badge.ShouldBe("2");

        trash.Items.First(c => c.Id == iconoclasm.Id).IsChecked = true;
        await trash.RestoreAsync();
        await Until(() => page.NewConcepts.Count == 1);
        page.NewConcepts.Single().Id.ShouldBe(iconoclasm.Id);

        trash.Items.Single().IsChecked = true;
        await trash.DeletePermanentlyAsync();
        confirm.Asked.Count.ShouldBe(1);
        (await h.Get<IConceptStore>().GetAsync(greek.Id, CancellationToken.None)).ShouldBeNull();
        trash.Badge.ShouldBeEmpty();
    }
}

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
        var row = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Concepts)!;
        row.Selected = row.Options.First(o => o.Model?.Id == "claude-sonnet-5-5");
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
        fake.Requests[1].User.ShouldContain("- " + first[19]);
        fake.Requests[1].System.ShouldBe(fake.Requests[0].System, "the cached part must not change between batches");
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
    public async Task ACutOffBatchKeepsItsCompleteConceptsAndStopsTheRun()
    {
        var full = FakeChat.Json(FakeChat.Distinct(5));
        var starts = new List<int>();
        for (var at = full.IndexOf("{\"baslik\"", StringComparison.Ordinal); at >= 0; at = full.IndexOf("{\"baslik\"", at + 1, StringComparison.Ordinal))
        {
            starts.Add(at);
        }

        var fake = new FakeChat()
            .Answer(_ => throw new ProviderException("cut")
            {
                Code = "truncated",
                ChargedUsage = new ChatUsage(1000, 0, 0, 36_000),
                PartialText = full[..(starts[3] + 12)],
            });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        await PrepareAsync(h);
        var page = await PageAsync(h);
        page.Count = 20;

        await page.GenerateAsync();

        page.NewConcepts.Count.ShouldBe(3);
        page.Missing.ShouldBe(17);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None)).Single();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldBe("truncated");
        run.Accepted.ShouldBe(3);
        run.OutputTokens.ShouldBe(36_000);
    }

    [AvaloniaFact]
    public async Task TheBatchBudgetFollowsTheTargetLength()
    {
        var fake = new FakeChat().Concepts(FakeChat.Distinct(20));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var profile = await PrepareAsync(h);
        profile.ContentLanguage = ContentLanguages.Turkish;
        profile.TargetSeconds = ProfileLimits.SecondsMax;
        await h.Get<IProfileStore>().SaveAsync(profile, CancellationToken.None);
        await h.Get<AppSession>().SelectProfileAsync(profile, CancellationToken.None);
        var page = await PageAsync(h);
        page.Count = 20;

        await page.GenerateAsync();

        // 20 Turkish 180 s scripts: about 1.7k visible tokens each, plus room for thinking (the old fixed 36k cut such batches off).
        fake.Requests.Single().MaxOutputTokens.ShouldBeGreaterThan(45_000);
        fake.Requests.Single().MaxOutputTokens.ShouldBeLessThanOrEqualTo(ConceptOutputBudget.Ceiling);
    }

    [AvaloniaFact]
    public async Task AMeasuredAverageIsNotMultipliedByTheThinkingShareAgain()
    {
        using var h = new DesktopHarness();
        var profile = await PrepareAsync(h);
        var generator = h.Get<ConceptGenerator>();
        var model = h.Get<TMConcept.Core.Models.IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;

        var first = await generator.EstimateAsync(profile, 20, string.Empty, model, CancellationToken.None);
        var guess = ConceptOutputBudget.TokensPerConcept(profile.ContentLanguage, profile.TargetSeconds) * 20;
        first.OutputTokensMax.ShouldBeInRange((long)(guess * ConceptGenerator.ThinkingShare.High) - 1, (long)(guess * ConceptGenerator.ThinkingShare.High) + 1);

        await h.Get<IRunStore>().AddAsync(new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.Concepts,
            Status = RunStatus.Succeeded,
            Accepted = 20,
            OutputTokens = 20 * 2_000,
            StartedAtUtc = DateTimeOffset.UtcNow,
        }, CancellationToken.None);
        var measured = await generator.EstimateAsync(profile, 20, string.Empty, model, CancellationToken.None);
        measured.OutputTokensMin.ShouldBeInRange((long)(20 * 2_000 * ConceptGenerator.MeasuredBand.Low) - 1, (long)(20 * 2_000 * ConceptGenerator.MeasuredBand.Low) + 1);
        measured.OutputTokensMax.ShouldBeInRange((long)(20 * 2_000 * ConceptGenerator.MeasuredBand.High) - 1, (long)(20 * 2_000 * ConceptGenerator.MeasuredBand.High) + 1);
    }

    [AvaloniaFact]
    public async Task PermanentDeleteSkipsHiddenCardsAndBacksUpFirstWhenItIsLarge()
    {
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s => s.AddSingleton<IConfirmDialog>(confirm));
        var profile = await PrepareAsync(h);
        var trashed = Enumerable.Range(1, 25).Select(i => new Concept
        {
            ProfileId = profile.Id,
            Title = (i == 1 ? "Hidden topic " : "Visible topic ") + Guid.NewGuid().ToString("N"),
            Summary = "s",
            Content = "c",
            Language = "en",
            Status = ConceptStatus.Trashed,
        }).ToList();
        await h.Get<IConceptStore>().AddAsync(trashed, CancellationToken.None);
        var trash = h.Get<TrashViewModel>();
        await trash.ActivateAsync();
        foreach (var card in trash.Items)
        {
            card.IsChecked = true;
        }

        trash.Search = "Visible";
        trash.Items.Count.ShouldBe(24);

        await trash.DeletePermanentlyAsync();

        (await h.Get<IConceptStore>().GetAsync(trashed[0].Id, CancellationToken.None)).ShouldNotBeNull("a card hidden by the search is not deleted");
        (await h.Get<IConceptStore>().GetAsync(trashed[1].Id, CancellationToken.None)).ShouldBeNull();
        Directory.EnumerateFiles(h.Paths.BackupDirectory, TMConcept.Storage.Backup.BackupRules.ChangePrefix + "*").ShouldNotBeEmpty();
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

    [AvaloniaFact]
    public async Task TheMeaningCheckEstimateCountsTheEarlierConceptsItEmbedsFirst()
    {
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(new FakeChat()));
        var profile = await PrepareAsync(h);
        await h.Get<IConceptStore>().AddAsync(FakeChat.Distinct(30).Select(t => new Concept { ProfileId = profile.Id, Title = t, Summary = "s", Language = "en" }).ToList(),
            CancellationToken.None);
        var generator = h.Get<ConceptGenerator>();
        var catalog = h.Get<TMConcept.Core.Models.IModelCatalog>();
        var online = catalog.Find("openai", "text-embedding-3-small")!;
        var local = catalog.Find(TMConcept.Core.Models.ModelProviders.Local, "paraphrase-multilingual-minilm")!;

        var paid = await generator.EstimateEmbeddingAsync(profile, online, 10, CancellationToken.None);
        var free = await generator.EstimateEmbeddingAsync(profile, local, 10, CancellationToken.None);

        // 10 new + 30 earlier concepts without a vector, 80 tokens each (English).
        paid.Backfill.ShouldBe(30);
        paid.Estimate.InputTokens.ShouldBe(40 * ConceptGenerator.EmbeddingTokensPerConcept);
        paid.Estimate.MaxUsd.ShouldBe(40 * ConceptGenerator.EmbeddingTokensPerConcept * online.InputUsd / 1_000_000m);
        free.Backfill.ShouldBe(30);
        free.Estimate.MaxUsd.ShouldBe(0);

        // The page says so next to the estimate.
        var page = await PageAsync(h);
        var row = page.Picker.Row(TMConcept.Core.Pipeline.PipelineSteps.Embedding)!;
        row.Selected = row.Options.First(o => o.Model?.Id == local.Id);
        await Until(() => page.Estimate.Contains(h.Get<TMConcept.Desktop.Localization.Localizer>().Format("concepts.estimate.backfill", 30), StringComparison.Ordinal));
    }
}

using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Core;

/// <summary>A <see cref="TopicBrief"/>: one concept per ticked topic, in order, each batch with its own slice, no extra request (fake model only).</summary>
public sealed class TopicGenerationTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static IReadOnlyList<string> Topics(int count) => Enumerable.Range(1, count).Select(i => "Topic " + i).ToList();

    private static async Task<(Profile Profile, ResolvedModel Model)> PrepareAsync(DesktopHarness h)
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = "fake-test-key-0123456789abcdef" }, None);

        // An earlier run that measured short concepts, so a batch of 20 fits its max_tokens.
        await h.Get<IRunStore>().AddAsync(new Run
        {
            ProfileId = profile.Id,
            Kind = RunKind.Concepts,
            Status = RunStatus.Succeeded,
            Accepted = 20,
            OutputTokens = 20 * 1_000,
            StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            FinishedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
        }, None);
        var sonnet = h.Get<IModelCatalog>().Find("anthropic", "claude-sonnet-5-5")!;
        var key = (await h.Get<IApiKeyStore>().ListAsync("anthropic", None)).First();
        return (profile, new ResolvedModel(sonnet, key, string.Empty));
    }

    private static string Block(int from, int to) =>
        "TOPICS (one concept each, in order):" + string.Concat(Enumerable.Range(from, to - from + 1).Select(i => $"\n{i - from + 1}. «Topic {i}»"));

    [AvaloniaFact]
    public async Task FortyFiveTopicsRunAsTwentyTwentyFiveAndEveryBatchCarriesItsSliceAndNoExtraRequest()
    {
        var fake = new FakeChat().Concepts(FakeChat.Distinct(20)).Concepts(FakeChat.Distinct(20)).Concepts(FakeChat.Distinct(5));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new TopicBrief(Topics(45)), model), null, null, None);

        result.Run.Requested.ShouldBe(45, "the count is the number of topics");
        result.Run.ExtraPrompt.ShouldBeEmpty();
        result.Accepted.Count.ShouldBe(45);
        fake.Requests.Count.ShouldBe(3);
        fake.Requests[0].User.ShouldContain(Block(1, 20) + "\nREQUESTED: 20 new concepts.");
        fake.Requests[1].User.ShouldContain(Block(21, 40) + "\nREQUESTED: 20 new concepts.");
        fake.Requests[2].User.ShouldContain(Block(41, 45) + "\nREQUESTED: 5 new concepts.");
        fake.Requests[1].User.ShouldNotContain("«Topic 20»");
        fake.Requests.ShouldAllBe(r => !r.User.Contains("EXTRA REQUEST"), "a topic run sends no extra request (0.1.12 leaked it)");
    }

    [AvaloniaFact]
    public async Task AnIdeaRunSendsItsIdeaAndCountAndNeverTopics()
    {
        var fake = new FakeChat().Concepts(FakeChat.Distinct(3));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new IdeaBrief("  a calm tone ", 3), model), null, null, None);

        result.Run.Requested.ShouldBe(3);
        result.Run.ExtraPrompt.ShouldBe("a calm tone");
        var user = fake.Requests.Single().User;
        user.ShouldContain("EXTRA REQUEST: «a calm tone»");
        user.ShouldContain("REQUESTED: 3 new concepts.");
        user.ShouldNotContain("TOPICS (one concept each");
    }

    [AvaloniaFact]
    public async Task ACutSecondBatchReportsOnlyTheTopicsOfItsCompleteConcepts()
    {
        var full = FakeChat.Json(FakeChat.Distinct(8));
        var starts = new List<int>();
        for (var at = full.IndexOf("{\"baslik\"", StringComparison.Ordinal); at >= 0; at = full.IndexOf("{\"baslik\"", at + 1, StringComparison.Ordinal))
        {
            starts.Add(at);
        }

        var fake = new FakeChat()
            .Concepts(FakeChat.Distinct(20))
            .Answer(_ => throw new ProviderException("cut")
            {
                Code = "truncated",
                ChargedUsage = new ChatUsage(1000, 0, 0, 30_000),
                PartialText = full[..(starts[7] + 12)],
            });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);
        var batches = new List<ConceptBatch>();

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new TopicBrief(Topics(45)), model),
            new ForwardProgress<ConceptBatch>(batches.Add), null, None);

        result.ErrorCode.ShouldBe("truncated");
        result.Accepted.Count.ShouldBe(27);
        batches.SelectMany(b => b.TopicsMade).ShouldBe(Enumerable.Range(1, 27).Select(i => "Topic " + i).ToList(),
            "7 of the second batch came back; the page keeps topics 28-45 ticked");
        fake.Requests.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task EverySavedBatchReportsTheTopicsItMadeAndAFailedBatchNone()
    {
        var fake = new FakeChat()
            .Concepts(FakeChat.Distinct(20))
            .Answer(_ => throw new ProviderException("overloaded") { Code = "http", ChargedUsage = new ChatUsage(0, 0, 0, 0) });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);
        var batches = new List<ConceptBatch>();

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new TopicBrief(Topics(45)), model),
            new ForwardProgress<ConceptBatch>(batches.Add), null, None);

        result.ErrorCode.ShouldBe("http");
        batches.Single().TopicsMade.ShouldBe(Enumerable.Range(1, 20).Select(i => "Topic " + i).ToList(),
            "the page takes these off its list even when a later batch fails");
    }

    [Fact]
    public void TopicBriefRefusesMoreThan100AndNone()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new TopicBrief(Topics(101)), "never cut silently");
        Should.Throw<ArgumentOutOfRangeException>(() => new TopicBrief([]));
        new TopicBrief(Topics(100)).Count.ShouldBe(100);
    }

    [AvaloniaFact]
    public async Task EstimateOfATopicBriefCountsTheTopics()
    {
        using var h = new DesktopHarness();
        var (profile, model) = await PrepareAsync(h);
        var generator = h.Get<ConceptGenerator>();

        var idea = await generator.EstimateAsync(profile, new IdeaBrief(string.Empty, 45), string.Empty, model.Model, string.Empty, None);
        var topics = await generator.EstimateAsync(profile, new TopicBrief(Topics(45)), string.Empty, model.Model, string.Empty, None);
        var fewer = await generator.EstimateAsync(profile, new TopicBrief(Topics(3)), string.Empty, model.Model, string.Empty, None);

        topics.MaxUsd.ShouldBeGreaterThan(idea.MaxUsd, "45 concepts either way, and every batch's prompt also carries its topics");
        topics.MinUsd.ShouldBeGreaterThan(idea.MinUsd);
        fewer.MaxUsd.ShouldBeLessThan(topics.MaxUsd, "3 ticked topics are 3 concepts");
    }
}

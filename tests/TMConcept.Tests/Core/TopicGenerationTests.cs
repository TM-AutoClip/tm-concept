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

/// <summary>"Generate" with selected topics: one concept per topic, in order, each batch with its own slice (fake model only).</summary>
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
    public async Task FortyFiveTopicsRunAsTwentyTwentyFiveAndEveryBatchCarriesItsSlice()
    {
        var fake = new FakeChat().Concepts(FakeChat.Distinct(20)).Concepts(FakeChat.Distinct(20)).Concepts(FakeChat.Distinct(5));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, 3, "a calm tone", model, Topics: Topics(45)), null, null, None);

        result.Run.Requested.ShouldBe(45, "the count comes from the topics, not from the Count box");
        result.Accepted.Count.ShouldBe(45);
        result.TopicsLeft.ShouldBeEmpty();
        fake.Requests.Count.ShouldBe(3);
        fake.Requests[0].User.ShouldContain(Block(1, 20) + "\nREQUESTED: 20 new concepts.");
        fake.Requests[1].User.ShouldContain(Block(21, 40) + "\nREQUESTED: 20 new concepts.");
        fake.Requests[2].User.ShouldContain(Block(41, 45) + "\nREQUESTED: 5 new concepts.");
        fake.Requests[1].User.ShouldNotContain("«Topic 20»");
        fake.Requests.ShouldAllBe(r => r.User.Contains("EXTRA REQUEST: «a calm tone»"), "the extra request still goes along, for angle and tone");
    }

    [AvaloniaFact]
    public async Task ACutSecondBatchLeavesTheTopicsAfterItsCompleteConcepts()
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

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, 45, string.Empty, model, Topics: Topics(45)), null, null, None);

        result.ErrorCode.ShouldBe("truncated");
        result.Accepted.Count.ShouldBe(27);
        result.TopicsLeft.ShouldBe(Enumerable.Range(28, 18).Select(i => "Topic " + i).ToList(), "7 of the second batch came back; the third batch never ran");
        fake.Requests.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task AFailedBatchLeavesItsWholeSliceAndTheRest()
    {
        var fake = new FakeChat()
            .Concepts(FakeChat.Distinct(20))
            .Answer(_ => throw new ProviderException("overloaded") { Code = "http", ChargedUsage = new ChatUsage(0, 0, 0, 0) });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, 45, string.Empty, model, Topics: Topics(45)), null, null, None);

        result.ErrorCode.ShouldBe("http");
        result.TopicsLeft.ShouldBe(Enumerable.Range(21, 25).Select(i => "Topic " + i).ToList());
    }

    [AvaloniaFact]
    public async Task EverySavedBatchReportsTheTopicsItMade()
    {
        var fake = new FakeChat()
            .Concepts(FakeChat.Distinct(20))
            .Answer(_ => throw new ProviderException("overloaded") { Code = "http", ChargedUsage = new ChatUsage(0, 0, 0, 0) });
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);
        var batches = new List<ConceptBatch>();

        await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, 45, string.Empty, model, Topics: Topics(45)),
            new ForwardProgress<ConceptBatch>(batches.Add), null, None);

        batches.Single().TopicsMade.ShouldBe(Enumerable.Range(1, 20).Select(i => "Topic " + i).ToList(),
            "the page marks these made even when a later batch fails in an unexpected way");
    }

    [AvaloniaFact]
    public async Task OneHundredTwentyTopicsRunTheFirstHundred()
    {
        var fake = new FakeChat();
        for (var i = 0; i < 5; i++)
        {
            fake.Concepts(FakeChat.Distinct(20));
        }

        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, 1, string.Empty, model, Topics: Topics(120)), null, null, None);

        result.Run.Requested.ShouldBe(ConceptLimits.CountMax);
        result.Accepted.Count.ShouldBe(100);
        result.TopicsLeft.ShouldBeEmpty("the topics past 100 were not given to this run: the page keeps them selected");
        fake.Requests.Count.ShouldBe(5);
        fake.Requests[^1].User.ShouldContain(Block(81, 100));
        fake.Requests[^1].User.ShouldNotContain("«Topic 101»");
    }

    [AvaloniaFact]
    public async Task TheEstimateWithTopicsSendsTheirSlicesAndIgnoresTheCount()
    {
        using var h = new DesktopHarness();
        var (profile, model) = await PrepareAsync(h);
        var generator = h.Get<ConceptGenerator>();

        var plain = await generator.EstimateAsync(profile, 45, string.Empty, string.Empty, model.Model, string.Empty, None);
        var topics = await generator.EstimateAsync(profile, 3, string.Empty, string.Empty, model.Model, string.Empty, None, Topics(45));
        var sameCount = await generator.EstimateAsync(profile, 45, string.Empty, string.Empty, model.Model, string.Empty, None, Topics(45));

        topics.ShouldBe(sameCount, "with topics the count is the number of topics");
        topics.MaxUsd.ShouldBeGreaterThan(plain.MaxUsd, "every batch's prompt also carries its topics");
        topics.MinUsd.ShouldBeGreaterThan(plain.MinUsd);
        (await generator.EstimateAsync(profile, 1, string.Empty, string.Empty, model.Model, string.Empty, None, Topics(120)))
            .ShouldBe(await generator.EstimateAsync(profile, 1, string.Empty, string.Empty, model.Model, string.Empty, None, Topics(100)),
                "only the first 100 run");
    }
}

using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Core.Settings;
using TMConcept.Tests.Desktop;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Core;

/// <summary>
/// An answer that is not readable JSON is asked again (the same request, at most twice), every attempt is counted in the run's cost, and
/// three unreadable answers end with "parse". Topics, concepts and the AI edit; the topic answer limit comes from the settings. Fake model only.
/// </summary>
public sealed class ModelRetryTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly ChatUsage Usage = new(600, 0, 0, 120);

    private static ChatResponse Answer(string text) => new(text, Usage, null, "end_turn", "r");

    private static async Task<(Profile Profile, ResolvedModel Model)> PrepareAsync(DesktopHarness h, string model = "claude-haiku-5-5")
    {
        await FirstRunTests.CreateAsync(h);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = "fake-test-key-0123456789abcdef" }, None);
        var catalog = h.Get<IModelCatalog>().Find("anthropic", model)!;
        var key = (await h.Get<IApiKeyStore>().ListAsync("anthropic", None)).First();
        return (profile, new ResolvedModel(catalog, key, string.Empty));
    }

    [AvaloniaFact]
    public async Task TopicsAreAskedAgainUntilTheAnswerIsReadableAndEveryAttemptIsPaid()
    {
        var fake = new FakeChat().Answer(_ => Answer("Sorry, here are some topics: Greek fire, Nika riots"))
            .Answer(_ => Answer("<think>hmm</think>"))
            .Answer(_ => Answer("```json\n{\"scope\": \"x\", \"topics\": [\"Greek fire\", \"The Nika riots\",],}\n```\nEnjoy!"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var result = await h.Get<TopicSuggester>().SuggestAsync(profile, model, string.Empty, 10, "en", null, None);

        result.Topics.Select(t => t.Text).ShouldBe(["Greek fire", "The Nika riots"]);
        fake.Requests.Count.ShouldBe(3);
        fake.Requests.Select(r => r.User).Distinct().ShouldHaveSingleItem("the same request every time");
        var one = h.Get<ICostCalculator>().Exact(model.Model, Usage);
        result.CostUsd.ShouldBe(3 * one, "the two unreadable answers were paid too");
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).Single(r => r.Kind == RunKind.Topics);
        (run.Status, run.InputTokens, run.OutputTokens).ShouldBe((RunStatus.Succeeded, 3 * Usage.Input, 3 * Usage.Output));
    }

    [AvaloniaFact]
    public async Task ThreeUnreadableTopicAnswersFailWithParse()
    {
        var fake = new FakeChat().Answer(_ => Answer("no")).Answer(_ => Answer("still no")).Answer(_ => Answer("{\"topics\": 42}"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);

        var error = await Should.ThrowAsync<ProviderException>(() => h.Get<TopicSuggester>().SuggestAsync(profile, model, string.Empty, 10, "en", null, None));

        error.Code.ShouldBe("parse");
        fake.Requests.Count.ShouldBe(3);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).Single(r => r.Kind == RunKind.Topics);
        (run.Status, run.Error).ShouldBe((RunStatus.Failed, "parse"));
        run.ActualUsd.ShouldBe(3 * h.Get<ICostCalculator>().Exact(model.Model, Usage));
    }

    [AvaloniaFact]
    public async Task TheTopicAnswerLimitComesFromTheSettings()
    {
        var fake = new FakeChat().Answer(_ => Answer("{\"topics\": [\"Greek fire\"]}")).Answer(_ => Answer("{\"topics\": [\"Iconoclasm\"]}"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h);
        var suggester = h.Get<TopicSuggester>();

        await suggester.SuggestAsync(profile, model, string.Empty, 10, "en", null, None);
        fake.Requests[0].MaxOutputTokens.ShouldBe(TopicSuggester.OutputMax, "16 000 by default");

        var settings = await h.Get<ISettingsStore>().GetAsync(None);
        settings.TopicMaxOutputTokens.ShouldBe(16_000);
        settings.TopicMaxOutputTokens = 5_000;
        await h.Get<ISettingsStore>().SaveAsync(settings, None);
        await suggester.SuggestAsync(profile, model, string.Empty, 10, "en", null, None);
        fake.Requests[1].MaxOutputTokens.ShouldBe(5_000);

        TopicSuggester.ClampOutput(500).ShouldBe(TopicLimits.OutputMin);
        TopicSuggester.ClampOutput(0).ShouldBe(TopicSuggester.OutputMax);
        TopicSuggester.ClampOutput(1_000_000).ShouldBe(TopicLimits.OutputLimit);
        TopicSuggester.OutputFor(new CatalogModel { MaxOutputTokens = 8_000 }, 40_000).ShouldBe(8_000, "the model's own limit is lower");
    }

    [AvaloniaFact]
    public async Task AConceptBatchWithoutAReadableConceptIsAskedAgain()
    {
        var fake = new FakeChat().Answer(_ => Answer("I will write the concepts now.")).Concepts(FakeChat.Distinct(3));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h, "claude-sonnet-5-5");

        var result = await h.Get<ConceptGenerator>().GenerateAsync(new ConceptRequest(profile, new IdeaBrief(string.Empty, 3), model), null, null, None);

        result.Accepted.Count.ShouldBe(3);
        result.ErrorCode.ShouldBeEmpty();
        fake.Requests.Count.ShouldBe(2);
        fake.Requests[1].User.ShouldBe(fake.Requests[0].User, "the same request again");
        result.Run.InputTokens.ShouldBe(Usage.Input + 1000, "both attempts are charged");
    }

    [AvaloniaFact]
    public async Task AnAiEditWithoutAReadableConceptFailsAfterThreeAttempts()
    {
        var fake = new FakeChat().Answer(_ => Answer("x")).Answer(_ => Answer("y")).Answer(_ => Answer("{\"konseptler\": []}"));
        using var h = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(fake));
        var (profile, model) = await PrepareAsync(h, "claude-sonnet-5-5");
        var concept = new Concept { ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = "The story.", Language = "en" };
        await h.Get<IConceptStore>().AddAsync([concept], None);

        var result = await h.Get<ConceptEditor>().EditAsync(profile, concept, "shorter", model, null, null, None);

        result.Concept.ShouldBeNull();
        result.ErrorCode.ShouldBe("parse");
        fake.Requests.Count.ShouldBe(3);
        result.CostUsd.ShouldBe(3 * h.Get<ICostCalculator>().Exact(model.Model, Usage));
    }
}
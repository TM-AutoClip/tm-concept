using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Engines;
using TMConcept.Core.Keys;
using TMConcept.Core.Media;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Providers.Endpoints;
using TMConcept.Storage.Database;
using TMConcept.Storage.Stores;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Local servers in the app: Settings → API keys adds, tests and deletes them; the store keeps them in settings rows (the token apart); the
/// resolver needs no key for them; the pickers offer them free with no key asked; topics are suggested through one (fake server only).
/// </summary>
public sealed class EndpointSettingsTests
{
    private const string Token = "local-test-token-0123456789";
    private static readonly CancellationToken None = CancellationToken.None;

    private static DesktopHarness Harness(FakeEndpointServer server, Action<IServiceCollection>? more = null) => new(s =>
    {
        s.AddSingleton<EndpointTransport>(server.Transport);
        more?.Invoke(s);
    });

    private static async Task<LocalEndpoint> SaveAsync(DesktopHarness h, FakeEndpointServer server, string token = Token)
    {
        var endpoint = new LocalEndpoint
        {
            Name = "Lemonade",
            BaseUrl = server.BaseUrl,
            Token = token,
            Models = [new EndpointModel { Id = "bonsai-27b", ContextTokens = 32768, MaxOutputTokens = 8192 }],
        };
        await h.Get<EndpointRegistry>().SaveAsync(endpoint, None);
        return endpoint;
    }

    [AvaloniaFact]
    public async Task TheKeysPageAddsTestsAndDeletesALocalServer()
    {
        using var server = new FakeEndpointServer("api/v1");
        var confirm = new FakeConfirm();
        using var h = Harness(server, s => s.AddSingleton<IConfirmDialog>(confirm));
        await FirstRunTests.CreateAsync(h);
        var keys = h.Get<KeysSettingsViewModel>();
        var text = h.Get<Localizer>();
        await keys.ActivateAsync();
        keys.IsEndpoint.ShouldBeFalse();
        keys.Provider = keys.Providers.Single(p => p.IsEndpoint);
        keys.Provider.Name.ShouldBe(text["keys.endpoint.provider"]);
        keys.IsEndpoint.ShouldBeTrue();
        keys.Guide.ShouldContain("http://localhost:13305/api/v1");
        keys.Guide.ShouldContain("http://localhost:11434/v1");
        var editor = keys.Endpoint;

        // Saving without a model is refused in red; the server's models are listed with its context.
        editor.Name = "Lemonade";
        editor.Address = server.BaseUrl + "/";
        editor.Token = Token;
        await editor.SaveAsync();
        (editor.Failed, editor.Status).ShouldBe((true, "✗ " + text["keys.endpoint.no_model"]));
        await editor.ConnectAsync();
        editor.Failed.ShouldBeFalse();
        editor.Models.Select(m => (m.Id, m.ContextTokens, m.Picked)).ShouldBe([("bonsai-27b", (decimal?)32768m, false), ("qwen3-8b", (decimal?)8192m, false)]);
        editor.Models[0].MaxOutputTokens.ShouldBe(4096m);
        server.Requests.Single().Headers["Authorization"].ShouldBe("Bearer " + Token);

        // One listed and one typed model; JSON schema on the first.
        editor.Models[0].Picked = true;
        editor.Models[0].JsonSchema = true;
        editor.Models[0].MaxOutputTokens = 6000;
        editor.ManualModel = " typed-model ";
        editor.AddManual();
        await keys.AddAsync();

        editor.Failed.ShouldBeFalse(editor.Status);
        editor.Models.ShouldBeEmpty();
        editor.Token.ShouldBeEmpty();
        var saved = h.Get<EndpointRegistry>().All.Single();
        saved.BaseUrl.ShouldBe(server.BaseUrl);
        saved.Token.ShouldBe(Token);
        saved.Models.Select(m => (m.Id, m.MaxOutputTokens, m.JsonSchema)).ShouldBe([("bonsai-27b", 6000, true), ("typed-model", 4096, false)]);
        var row = editor.Saved.Single();
        row.Token.ShouldNotContain("0123456789");
        h.Get<IModelCatalog>().Find(EndpointCodes.For(saved.Id), "typed-model").ShouldNotBeNull();

        // The test names a kept model the server does not list; a refused token is said in red.
        await editor.TestAsync(row);
        row.Failed.ShouldBeFalse();
        row.Result.ShouldBe("✓ " + text.Format("keys.endpoint.not_listed", "typed-model"));
        server.ModelList(401, """{"error":{"message":"bad token"}}""");
        await editor.TestAsync(row);
        (row.Failed, row.Result).ShouldBe((true, "✗ " + text["keys.endpoint.fail.auth"]));

        // Delete asks first; a declined one keeps it.
        confirm.Answers.Enqueue(false);
        await editor.DeleteAsync(row);
        h.Get<EndpointRegistry>().All.Count.ShouldBe(1);
        await editor.DeleteAsync(row);
        confirm.Asked.ShouldBe([text["keys.endpoint.delete.title"], text["keys.endpoint.delete.title"]]);
        h.Get<EndpointRegistry>().All.ShouldBeEmpty();
        editor.Saved.ShouldBeEmpty();
        (await h.Get<IEndpointStore>().ListAsync(None)).ShouldBeEmpty();
        h.Get<IModelCatalog>().Find(EndpointCodes.For(saved.Id), "typed-model").ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task ConnectionProblemsAreSaidInRed()
    {
        string gone;
        using (var stopped = new FakeEndpointServer())
        {
            gone = stopped.BaseUrl;
        }

        using var server = new FakeEndpointServer();
        server.ModelList(200, """{"data":[]}""");
        using var h = Harness(server);
        await FirstRunTests.CreateAsync(h);
        var editor = h.Get<KeysSettingsViewModel>().Endpoint;
        var text = h.Get<Localizer>();

        editor.Address = "localhost:13305";
        await editor.ConnectAsync();
        (editor.Failed, editor.Status).ShouldBe((true, "✗ " + text["keys.endpoint.fail.address"]));
        server.Requests.ShouldBeEmpty("an invalid address is not asked");

        editor.Address = gone;
        await editor.ConnectAsync();
        (editor.Failed, editor.Status).ShouldBe((true, "✗ " + text["keys.endpoint.fail.network"]));

        editor.Address = server.BaseUrl;
        await editor.ConnectAsync();
        (editor.Failed, editor.Status).ShouldBe((true, "✗ " + text["keys.endpoint.fail.empty"]));
    }

    [AvaloniaFact]
    public async Task TheStoreKeepsTheTokenApartAndSurvivesALoad()
    {
        using var server = new FakeEndpointServer();
        using var h = Harness(server);
        await FirstRunTests.CreateAsync(h);
        var endpoint = await SaveAsync(h, server);
        var keyless = new LocalEndpoint { Name = "Ollama", BaseUrl = "http://localhost:11434/v1", Models = [new EndpointModel { Id = "gemma3" }] };
        await h.Get<IEndpointStore>().SaveAsync(keyless, None);

        var list = await h.Get<IEndpointStore>().ListAsync(None);

        list.Select(e => (e.Id, e.Name, e.Token)).ShouldBe([(endpoint.Id, "Lemonade", Token), (keyless.Id, "Ollama", string.Empty)]);
        list[0].Models.Single().MaxOutputTokens.ShouldBe(8192);
        var rows = await QueryAsync(h, db => db.Settings.Where(s => s.Key == EndpointStore.EndpointsSetting || s.Key == EndpointStore.TokensSetting)
            .ToDictionaryAsync(s => s.Key, s => s.Value));
        rows[EndpointStore.EndpointsSetting].ShouldNotContain(Token, Case.Sensitive, "the endpoint list holds no token");
        rows[EndpointStore.TokensSetting].ShouldContain(Token);

        // After unlock the session reads them back into the catalog.
        var fresh = new EndpointRegistry(h.Get<IEndpointStore>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<EndpointRegistry>.Instance);
        await fresh.LoadAsync(None);
        fresh.All.Count.ShouldBe(2);
        await h.Get<AppSession>().LoadAsync(None);
        h.Get<IModelCatalog>().Find(EndpointCodes.For(keyless.Id), "gemma3").ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task TheResolverNeedsNoKeyAndTheGateAsksNone()
    {
        using var server = new FakeEndpointServer();
        FakeApiKeyDialog? dialog = null;
        using var h = Harness(server, s => s.AddSingleton<IApiKeyDialog>(p => dialog = new FakeApiKeyDialog(p.GetRequiredService<IApiKeyStore>())));
        await FirstRunTests.CreateAsync(h);
        var endpoint = await SaveAsync(h, server);
        var code = EndpointCodes.For(endpoint.Id);
        var resolver = h.Get<StepModelResolver>();
        var choice = new StepModelChoice { Step = PipelineSteps.Topics, Provider = code, Model = "bonsai-27b", OptionsJson = """{"Effort":"high"}""" };

        var resolved = await resolver.ResolveAsync(PipelineSteps.Topics, choice, None);

        resolved.Key.Secret.ShouldBe(Token);
        resolved.Key.Label.ShouldBe("Lemonade");
        resolved.Effort.ShouldBeEmpty("a local server has no effort levels");
        (await resolver.ProvidersWithoutWorkingKeyAsync([resolved.Model], None)).ShouldBeEmpty();
        (await resolver.ProvidersWithoutKeyAsync([resolved.Model], None)).ShouldBeEmpty();
        StepModelResolver.NeedsKey(code).ShouldBeFalse();
        StepModelResolver.NeedsKey("anthropic").ShouldBeTrue();

        // The picker offers it free, sorted before the online models, and starting asks no key.
        var profile = new Profile { Name = "P", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        var page = h.Get<ConceptsViewModel>();
        await page.ActivateAsync();
        var picker = page.Settings.Picker;
        var row = picker.Row(PipelineSteps.Topics)!;
        var option = row.Options.Single(o => o.Model?.Provider == code);
        option.Label.ShouldBe("bonsai-27b · Lemonade · " + h.Get<Localizer>()["models.endpoint_free"]);
        row.Options.IndexOf(option).ShouldBeLessThan(row.Options.IndexOf(row.Options.First(o => o.Model?.Provider == "anthropic")));
        row.Selected = option;
        row.Efforts.Count.ShouldBeLessThanOrEqualTo(1);
        (await picker.PrepareAsync([PipelineSteps.Topics], "install.purpose.topics", None)).ShouldBeTrue(picker.Status);
        dialog!.Asked.ShouldBeEmpty();
        (await h.Get<IStepModelStore>().GetAsync(PipelineSteps.Topics, profile.Id, None))!.Provider.ShouldBe(code);

        // Deleted: the saved choice is not ready any more and the picker shows none.
        await h.Get<EndpointRegistry>().DeleteAsync(endpoint.Id, None);
        var missing = await Should.ThrowAsync<ModelNotReadyException>(() => resolver.ResolveForAsync(PipelineSteps.Topics, profile.Id, None));
        missing.Missing.ShouldBe("unknown");
        await page.ActivateAsync();
        picker.Row(PipelineSteps.Topics)!.Selected!.Model.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task TopicsAreSuggestedThroughALocalServer()
    {
        using var server = new FakeEndpointServer();
        server.Chat(503, """{"error":{"message":"loading"}}""")
            .Chat(200, FakeEndpointServer.Stream("<think>Byzantium {1453}</think>{\"scope\":\"Byzantium\",\"topics\":[\"Greek fire\",\"The Nika riots\"]}"),
                "text/event-stream");
        using var h = Harness(server);
        await FirstRunTests.CreateAsync(h);
        var endpoint = await SaveAsync(h, server);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "Byzantine history." };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        var choice = new StepModelChoice { Step = PipelineSteps.Topics, Provider = EndpointCodes.For(endpoint.Id), Model = "bonsai-27b" };
        var model = await h.Get<StepModelResolver>().ResolveAsync(PipelineSteps.Topics, choice, None);

        var result = await h.Get<TopicSuggester>().SuggestAsync(profile, model, string.Empty, 10, "en", null, None);

        result.Topics.Select(t => t.Text).ShouldBe(["Greek fire", "The Nika riots"]);
        (result.CostUsd, result.CostExact).ShouldBe((0m, true));
        var chats = server.Chats;
        chats.Count.ShouldBe(2, "the 503 while the model loaded was retried");
        var body = JsonNode.Parse(chats[1].Body)!;
        body["max_tokens"]!.GetValue<int>().ShouldBe(8192, "the topic call asks 16 000; the model allows 8 192");
        body["response_format"]!["type"]!.ToString().ShouldBe("json_object");
        body["reasoning_effort"].ShouldBeNull();
        chats[1].Headers["Authorization"].ShouldBe("Bearer " + Token);
        var run = (await h.Get<IRunStore>().ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).Single(r => r.Kind == RunKind.Topics);
        (run.Provider, run.Model, run.KeyLabel, run.ActualUsd, run.Status).ShouldBe((choice.Provider, "bonsai-27b", "Lemonade", 0m, RunStatus.Succeeded));
    }

    [AvaloniaFact]
    public async Task ALocalServerThatRefusesTheVideoCodeCallStopsTheVideoAndSaysWhy()
    {
        // The stop rule (16 Nov 2026) holds for a local server too: its failure fails the queue row in red with the server's reason, the
        // activity panel gets a red line, no video is kept and the concept stays queued for a retry.
        using var server = new FakeEndpointServer();
        server.Chat(401, """{"error":{"message":"bad token"}}""");
        var engine = new VideoPageTests.FakeEngine();
        using var h = Harness(server, s =>
        {
            s.RemoveAll<IVideoEngine>();
            s.AddSingleton<IVideoEngine>(engine);
            s.AddSingleton<IMediaTools, VideoPageTests.FakeMedia>();
        });
        await FirstRunTests.CreateAsync(h);
        var endpoint = await SaveAsync(h, server);
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English, ConceptText = "x" };
        await h.Get<IProfileStore>().SaveAsync(profile, None);
        await h.Get<AppSession>().SelectProfileAsync(profile, None);
        var concept = new Concept
        {
            ProfileId = profile.Id, Title = "Greek fire", Summary = "s", Content = "[on screen: a map]\nIn 672 the city was under siege.", Language = "en",
            Status = ConceptStatus.Approved,
        };
        await h.Get<IConceptStore>().AddAsync([concept], None);
        await h.Get<IStepModelStore>().SaveAsync(new StepModelChoice
        {
            Step = PipelineSteps.VideoCode, ProfileId = profile.Id, Provider = EndpointCodes.For(endpoint.Id), Model = "bonsai-27b",
        }, None);
        var page = h.Get<VideoViewModel>();
        await page.ActivateAsync();
        await page.EnqueueAsync(page.Approved.Single());
        var row = page.Queue.Single();

        await page.ProduceAllAsync();

        server.Chats.Count.ShouldBe(1, "a refused token is not retried");
        row.Failed.ShouldBeTrue();
        row.Error.ShouldNotBeNullOrWhiteSpace();
        h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent().ShouldContain(l => l.IsError && l.Text.Contains("Greek fire", StringComparison.Ordinal));
        (await h.Get<IVideoStore>().ListAsync(profile.Id, None)).ShouldBeEmpty();
        (await h.Get<IConceptStore>().GetAsync(concept.Id, None))!.Status.ShouldBe(ConceptStatus.Queued);
    }

    private static async Task<T> QueryAsync<T>(DesktopHarness h, Func<ConceptDbContext, Task<T>> query)
    {
        await using var db = await h.Get<ConceptDatabase>().OpenAsync(None);
        return await query(db);
    }
}

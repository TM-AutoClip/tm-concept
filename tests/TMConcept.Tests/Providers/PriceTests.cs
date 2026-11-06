using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Models;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Common;
using TMConcept.Providers.Pricing;
using TMConcept.Tests.Architecture;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Providers;

public sealed class PriceTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    private static string File(string date, params (string Provider, string Id, decimal Input)[] prices) => new JsonObject
    {
        ["schema"] = 1,
        ["pricedOn"] = date,
        ["models"] = new JsonArray(prices.Select(p => (JsonNode)new JsonObject { ["provider"] = p.Provider, ["id"] = p.Id, ["inputUsd"] = p.Input }).ToArray()),
    }.ToJsonString();

    private const string OpenRouter = """
        {"data":[
          {"id":"anthropic/claude-sonnet-5.5","pricing":{"prompt":"0.0000025","completion":"0.000012","input_cache_read":"0.00000025"}},
          {"id":"openrouter/auto","pricing":{"prompt":"-1","completion":"-1"}},
          {"id":"someone/unknown","pricing":{"prompt":"0.000001","completion":"0.000002"}}
        ]}
        """;

    [Fact]
    public void ARepositoryFileChangesKnownModelsOnlyAndDropsWhatIsOutOfRange()
    {
        var catalog = new EmbeddedModelCatalog();
        var json = File("2026-11-07", ("anthropic", "claude-sonnet-5-5", 2.5m), ("anthropic", "claude-opus-5-5", 4000m), ("anthropic", "claude-haiku-5-5", 50m),
            ("someone", "new-model", 1m), ("local", "supertonic", 1m));

        var table = PriceCheck.ReadFile(json, catalog, Today, out var problem, out var dropped)!;
        catalog.ApplyPrices([table]);

        problem.ShouldBeEmpty();
        dropped.ShouldBe(4, "out of range (4000), more than 20 times the built-in price (50 vs 0.10), unknown, local");
        catalog.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(2.5m);
        catalog.Find("anthropic", "claude-sonnet-5-5")!.OutputUsd.ShouldBe(10m, "a missing value keeps the built-in one");
        catalog.Find("anthropic", "claude-sonnet-5-5")!.PriceSource.ShouldBe(PriceSources.Repository);
        catalog.Find("anthropic", "claude-opus-5-5")!.InputUsd.ShouldBe(4m);
        catalog.Find("someone", "new-model").ShouldBeNull();
        catalog.Prices.Select(p => p.Source).ShouldBe([PriceSources.BuiltIn, PriceSources.Repository]);
    }

    [Theory]
    [InlineData("{\"schema\":2,\"pricedOn\":\"2026-11-07\",\"models\":[]}", "schema")]
    [InlineData("{\"schema\":1,\"pricedOn\":\"07.11.2026\",\"models\":[]}", "date")]
    [InlineData("{\"schema\":1,\"pricedOn\":\"2027-01-01\",\"models\":[]}", "date")]
    [InlineData("{\"schema\":1,\"pricedOn\":\"2026-01-01\",\"models\":[]}", "older")]
    [InlineData("not json", "JSON")]
    public void AFileWithAnotherSchemaABadDateOrBrokenJsonIsRefused(string json, string reason)
    {
        PriceCheck.ReadFile(json, new EmbeddedModelCatalog(), Today, out var problem, out _).ShouldBeNull();
        problem.ShouldContain(reason);
    }

    [Fact]
    public void OpenRouterPricesArePerTokenAndVariablePricesAreSkipped()
    {
        var catalog = new EmbeddedModelCatalog();

        var table = PriceCheck.ReadOpenRouter(OpenRouter, catalog, Today, out var problem, out _)!;

        problem.ShouldBeEmpty();
        table.PricedOn.ShouldBe("2026-11-07");
        var sonnet = table.Models.Single();
        sonnet.Id.ShouldBe("anthropic/claude-sonnet-5.5");
        sonnet.InputUsd.ShouldBe(2.5m);
        sonnet.OutputUsd.ShouldBe(12m);
        sonnet.CacheReadUsd.ShouldBe(0.25m);
        sonnet.CacheWriteUsd.ShouldBeNull();
    }

    [Fact]
    public async Task UpdatesRunOnceADayKeepTheLastGoodCopyAndWorkOffline()
    {
        using var paths = new TempPaths();
        var clock = new FakeTimeProvider(Today);
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, File("2026-11-07", ("anthropic", "claude-sonnet-5-5", 2.5m)))
            .Answer(HttpStatusCode.OK, OpenRouter);
        var catalog = new EmbeddedModelCatalog();
        var updater = new PriceUpdater(new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance), catalog, paths, clock, NullLogger<PriceUpdater>.Instance);

        (await updater.UpdateAsync(force: false, CancellationToken.None)).ShouldBeTrue();

        fake.Requests.Select(r => r.Uri).ShouldBe([PriceUpdater.RepositoryFile, PriceUpdater.OpenRouterModels]);
        fake.Requests.ShouldAllBe(r => !r.Headers.ContainsKey("Authorization"));
        catalog.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(2.5m);
        catalog.Find("openrouter", "anthropic/claude-sonnet-5.5")!.OutputUsd.ShouldBe(12m);

        // The same day: no request.
        clock.Advance(TimeSpan.FromHours(20));
        (await updater.UpdateAsync(force: false, CancellationToken.None)).ShouldBeFalse();
        fake.Requests.Count.ShouldBe(2);

        // The next day the repository is down and OpenRouter answers nonsense: the last good copies stay.
        clock.Advance(TimeSpan.FromHours(5));
        fake.Answer(HttpStatusCode.InternalServerError, "down").Answer(HttpStatusCode.OK, "<html>");
        (await updater.UpdateAsync(force: false, CancellationToken.None)).ShouldBeTrue();
        catalog.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(2.5m);

        // Offline start: the saved copies are used without the network.
        var offline = new EmbeddedModelCatalog();
        new PriceUpdater(new ProviderHttp(new FakeHttp().Transport, NullLogger<ProviderHttp>.Instance), offline, paths, clock, NullLogger<PriceUpdater>.Instance)
            .LoadSaved();
        offline.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(2.5m);
        offline.Prices.Select(p => p.Source).ShouldBe([PriceSources.BuiltIn, PriceSources.Repository, PriceSources.OpenRouter]);

        // Nothing saved and no network: the built-in table.
        using var empty = new TempPaths();
        var fresh = new EmbeddedModelCatalog();
        var failing = new FakeHttp();
        var none = new PriceUpdater(new ProviderHttp(failing.Transport, NullLogger<ProviderHttp>.Instance), fresh, empty, clock, NullLogger<PriceUpdater>.Instance);
        none.LoadSaved();
        failing.Answer(HttpStatusCode.ServiceUnavailable, "x").Answer(HttpStatusCode.ServiceUnavailable, "x");
        (await none.UpdateAsync(force: false, CancellationToken.None)).ShouldBeTrue();
        fresh.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(2.00m);
        fresh.Prices.Single().Source.ShouldBe(PriceSources.BuiltIn);
    }

    [Fact]
    public void TheRepositoryPricesFileIsTheBuiltInTable()
    {
        var catalog = new EmbeddedModelCatalog();
        var json = System.IO.File.ReadAllText(Path.Combine(RepositoryFiles.Root, "pricing", "prices.json"));

        var table = PriceCheck.ReadFile(json, catalog, Today, out var problem, out var dropped)!;

        problem.ShouldBeEmpty();
        dropped.ShouldBe(0);
        table.PricedOn.ShouldBe(catalog.Prices[0].PricedOn);
        foreach (var model in catalog.All.Where(m => m.Provider != ModelProviders.Local))
        {
            var price = table.Models.Single(p => p.Provider == model.Provider && p.Id == model.Id);
            (price.InputUsd ?? 0).ShouldBe(model.InputUsd, model.Provider + "/" + model.Id);
            (price.OutputUsd ?? 0).ShouldBe(model.OutputUsd, model.Provider + "/" + model.Id);
            (price.ImageUsd ?? 0).ShouldBe(model.ImageUsd, model.Provider + "/" + model.Id);
        }
    }

    [Fact]
    public void ASavedCopyOlderThanTheBuiltInTableIsNotApplied()
    {
        using var paths = new TempPaths();
        var catalog = new EmbeddedModelCatalog();
        var updater = new PriceUpdater(new ProviderHttp(new FakeHttp().Transport, NullLogger<ProviderHttp>.Instance), catalog, paths,
            new FakeTimeProvider(Today), NullLogger<PriceUpdater>.Instance);
        Directory.CreateDirectory(updater.Folder);
        System.IO.File.WriteAllText(Path.Combine(updater.Folder, "repository.json"), File("2026-01-01", ("anthropic", "claude-sonnet-5-5", 2.5m)));

        updater.LoadSaved();

        catalog.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(2.00m);
        catalog.Prices.Single().Source.ShouldBe(PriceSources.BuiltIn);
    }

    [Theory]
    [InlineData("{\"schema\":1,\"pricedOn\":\"2026-11-07\",\"models\":null}", -1)]
    [InlineData("{\"schema\":1,\"pricedOn\":\"2026-11-07\",\"models\":[null,{\"provider\":null,\"id\":null},{\"provider\":\"anthropic\",\"id\":\"claude-sonnet-5-5\",\"inputUsd\":2.5}]}", 2)]
    public void AMissingListOrBrokenEntriesAreRefusedWithoutAnException(string json, int dropped)
    {
        var table = PriceCheck.ReadFile(json, new EmbeddedModelCatalog(), Today, out var problem, out var count);

        if (dropped < 0)
        {
            table.ShouldBeNull();
            problem.ShouldContain("models");
        }
        else
        {
            table!.Models.Single().InputUsd.ShouldBe(2.5m);
            count.ShouldBe(dropped);
        }
    }

    [Fact]
    public void TheLimitIsTheBuiltInPriceNotTheOneInUse()
    {
        var catalog = new EmbeddedModelCatalog();
        catalog.ApplyPrices([PriceCheck.ReadFile(File("2026-11-07", ("anthropic", "claude-sonnet-5-5", 30m)), catalog, Today, out _, out _)!]);
        catalog.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(30m);

        // 500 is under 20 times the price in use (30) but 250 times the built-in one (2): dropped.
        PriceCheck.ReadFile(File("2026-11-08", ("anthropic", "claude-sonnet-5-5", 500m)), catalog, Today, out _, out var dropped)!.Models.ShouldBeEmpty();
        dropped.ShouldBe(1);
    }

    [Fact]
    public async Task ABrokenOrEmptyDownloadKeepsTheLastGoodCopyAndTheOtherSourceStillRuns()
    {
        using var paths = new TempPaths();
        var clock = new FakeTimeProvider(Today);
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, File("2026-11-07", ("anthropic", "claude-sonnet-5-5", 2.5m)))
            .Answer(HttpStatusCode.OK, OpenRouter);
        var catalog = new EmbeddedModelCatalog();
        var updater = new PriceUpdater(new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance), catalog, paths, clock, NullLogger<PriceUpdater>.Instance);
        (await updater.UpdateAsync(force: false, CancellationToken.None)).ShouldBeTrue();

        // A list of nulls, then an OpenRouter answer with no model of the catalog: both are refused, nothing throws.
        fake.Answer(HttpStatusCode.OK, "{\"schema\":1,\"pricedOn\":\"2026-11-08\",\"models\":null}")
            .Answer(HttpStatusCode.OK, "{\"data\":[{\"id\":\"someone/unknown\",\"pricing\":{\"prompt\":\"79228162514264337593543950335\"}}]}");
        (await updater.UpdateAsync(force: true, CancellationToken.None)).ShouldBeTrue();

        fake.Requests.Count.ShouldBe(4);
        catalog.Find("anthropic", "claude-sonnet-5-5")!.InputUsd.ShouldBe(2.5m);
        catalog.Prices.Single(p => p.Source == PriceSources.Repository).PricedOn.ShouldBe("2026-11-07");
        catalog.Find("openrouter", "anthropic/claude-sonnet-5.5")!.OutputUsd.ShouldBe(12m);

        // An absurd OpenRouter price for a known model is dropped, not an overflow.
        PriceCheck.ReadOpenRouter("{\"data\":[{\"id\":\"anthropic/claude-sonnet-5.5\",\"pricing\":{\"prompt\":\"79228162514264337593543950335\"}}]}",
            catalog, Today, out _, out var dropped)!.Models.ShouldBeEmpty();
        dropped.ShouldBe(1);
    }
}

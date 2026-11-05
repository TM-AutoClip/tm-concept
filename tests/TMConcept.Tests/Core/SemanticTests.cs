using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Common;
using TMConcept.Providers.Embeddings;
using TMConcept.Providers.OpenAICompatible;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Core;

public sealed class SemanticTests
{
    private static readonly DuplicateSubject Earlier = new(Guid.NewGuid(), 7, "Greek fire", "s");

    [Theory]
    [InlineData(new float[] { 1, 0, 0 }, "Duplicate")]
    [InlineData(new float[] { 0.85f, 0.527f, 0 }, "Similar")]
    [InlineData(new float[] { 0, 1, 0 }, "New")]
    public void CosineBands(float[] candidate, string expected)
    {
        var result = SemanticCheck.Check(candidate, [(Earlier, new float[] { 1, 0, 0 })]);

        result.Kind.ToString().ShouldBe(expected);
    }

    [Fact]
    public void VectorsSurviveTheByteRoundTrip()
    {
        float[] vector = [0.5f, -1.25f, 3e-5f];

        SemanticCheck.FromBytes(SemanticCheck.ToBytes(vector)).ShouldBe(vector);
    }

    [Fact]
    public void ALongHistoryIsCompressedToAFixedSize()
    {
        var titles = Enumerable.Range(1, 2000).Select(i => i % 4 == 0 ? $"Nika Riot — Part {i / 4}" : $"Topic {i} of the walls").ToList();

        var lines = HistoryCompressor.Lines(titles);

        lines.Count.ShouldBeLessThan(titles.Count / 4);
        lines.ShouldContain(l => l.StartsWith("Nika Riot: parts 1–", StringComparison.Ordinal));
        lines.TakeLast(HistoryCompressor.NewestFull).ShouldBe(titles.TakeLast(HistoryCompressor.NewestFull));
        HistoryCompressor.Lines(titles.Take(50).ToList()).ShouldBe(titles.Take(50));
    }

    [Fact]
    public async Task OpenAIEmbeddingsKeepTheOrderAndCountTheTokens()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """
            {"data":[{"index":1,"embedding":[0,1]},{"index":0,"embedding":[1,0]}],"usage":{"prompt_tokens":12,"total_tokens":12}}
            """);
        var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var service = new EmbeddingService([new OpenAIEmbeddings(new OpenAIProvider(http, NullLogger<OpenAIProvider>.Instance), http)]);
        var model = new EmbeddedModelCatalog().All.First(m => m.Provider == "openai" && m.Capabilities.HasFlag(ModelCapability.Embedding));

        var result = await service.EmbedAsync(new ResolvedModel(model, new ApiKey { Secret = "fake-test-key-embed-0123" }, string.Empty), ["a", "b"], CancellationToken.None);

        result.Vectors[0].ShouldBe([1f, 0f]);
        result.Vectors[1].ShouldBe([0f, 1f]);
        result.Tokens.ShouldBe(12);
        fake.Requests.Single().Uri.ShouldBe(new Uri("https://api.openai.com/v1/embeddings"));
    }

    [Fact]
    public async Task OpenRouterServesTheEmbeddingModelItsCatalogOffers()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, """{"data":[{"index":0,"embedding":[1,0]}],"usage":{"prompt_tokens":3}}""");
        var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var service = new EmbeddingService(
        [
            new OpenAIEmbeddings(new OpenAIProvider(http, NullLogger<OpenAIProvider>.Instance), http),
            new OpenRouterEmbeddings(new OpenRouterProvider(http, NullLogger<OpenRouterProvider>.Instance), http),
        ]);
        var model = new EmbeddedModelCatalog().All.First(m => m.Provider == "openrouter" && m.Capabilities.HasFlag(ModelCapability.Embedding));

        var result = await service.EmbedAsync(new ResolvedModel(model, new ApiKey { Secret = "fake-test-key-embed-0123" }, string.Empty), ["a"], CancellationToken.None);

        result.Vectors.Single().ShouldBe([1f, 0f]);
        fake.Requests.Single().Uri.ShouldBe(new Uri("https://openrouter.ai/api/v1/embeddings"));
    }
}

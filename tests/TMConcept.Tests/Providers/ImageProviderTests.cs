using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Images;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Common;
using TMConcept.Providers.Fal;
using TMConcept.Providers.Images;
using TMConcept.Providers.OpenAICompatible;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Providers;

public sealed class ImageProviderTests
{
    private const string Key = "fake-test-key-image-0123456789";
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");

    private static ResolvedModel Model(string provider, string id) =>
        new(new EmbeddedModelCatalog().Find(provider, id)!, new ApiKey { Provider = provider, Secret = Key }, string.Empty);

    [Fact]
    public async Task FalQueuesPollsAndDownloadsWithoutSendingTheKeyToTheImageHost()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, """{"request_id":"r1","status_url":"https://queue.fal.run/fal-ai/flux-2/requests/r1/status","response_url":"https://queue.fal.run/fal-ai/flux-2/requests/r1"}""")
            .Answer(HttpStatusCode.OK, """{"status":"IN_PROGRESS"}""")
            .Answer(HttpStatusCode.OK, """{"status":"COMPLETED"}""")
            .Answer(HttpStatusCode.OK, """{"images":[{"url":"https://v3.fal.media/files/x/image.png"}]}""")
            .Answer(HttpStatusCode.OK, string.Empty, "image/png", r => r.Content = new ByteArrayContent(Png));
        var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var engine = new FalImageEngine(new FalProvider(http, NullLogger<FalProvider>.Instance), http, fake.Transport, NullLogger<FalImageEngine>.Instance);
        using var paths = new TempPaths();
        var output = Path.Combine(paths.WorkDirectory, "1.png");

        var result = await engine.GenerateAsync(new ImageRequest("a dome", 1080, 1920, output, Model("fal", "fal-ai/flux-2/flash")), CancellationToken.None);

        File.ReadAllBytes(output).ShouldBe(Png);
        result.CostUsd.ShouldBe(0.01m);
        fake.Requests[0].Uri.ShouldBe(new Uri("https://queue.fal.run/fal-ai/flux-2/flash"));
        fake.Requests.Take(4).ShouldAllBe(r => r.Uri.Host == "queue.fal.run" && r.Headers["Authorization"] == "Key " + Key);
        fake.Requests[4].Uri.Host.ShouldBe("v3.fal.media");
        fake.Requests[4].Headers.ContainsKey("Authorization").ShouldBeFalse();
        JsonNode.Parse(fake.Requests[0].Body)!["image_size"]!["height"]!.GetValue<int>().ShouldBe(1920);
    }

    [Fact]
    public async Task FalRefusesAnImageFromAnUnknownHost()
    {
        var fake = new FakeHttp()
            .Answer(HttpStatusCode.OK, """{"status_url":"https://queue.fal.run/s","response_url":"https://queue.fal.run/r"}""")
            .Answer(HttpStatusCode.OK, """{"status":"COMPLETED"}""")
            .Answer(HttpStatusCode.OK, """{"images":[{"url":"https://example.com/i.png"}]}""");
        var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var engine = new FalImageEngine(new FalProvider(http, NullLogger<FalProvider>.Instance), http, fake.Transport, NullLogger<FalImageEngine>.Instance);
        using var paths = new TempPaths();

        var error = await Should.ThrowAsync<ProviderException>(() =>
            engine.GenerateAsync(new ImageRequest("x", 512, 512, Path.Combine(paths.WorkDirectory, "x.png"), Model("fal", "fal-ai/flux-2/flash")), CancellationToken.None));

        error.Code.ShouldBe("bad_request");
        fake.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task OpenAIImagesDecodeTheBase64Answer()
    {
        var fake = new FakeHttp().Answer(HttpStatusCode.OK, "{\"data\":[{\"b64_json\":\"" + Convert.ToBase64String(Png) + "\"}]}");
        var http = new ProviderHttp(fake.Transport, NullLogger<ProviderHttp>.Instance);
        var engine = new OpenAIImageEngine(new OpenAIProvider(http, NullLogger<OpenAIProvider>.Instance), http);
        using var paths = new TempPaths();
        var output = Path.Combine(paths.WorkDirectory, "o.png");
        var model = new EmbeddedModelCatalog().All.First(m => m.Provider == "openai" && m.Capabilities.HasFlag(ModelCapability.Image));

        await engine.GenerateAsync(new ImageRequest("a map", 1080, 1920, output, new ResolvedModel(model, new ApiKey { Secret = Key }, string.Empty)),
            CancellationToken.None);

        File.ReadAllBytes(output).ShouldBe(Png);
        JsonNode.Parse(fake.Requests.Single().Body)!["size"]!.ToString().ShouldBe("1024x1536");
    }

    [Fact]
    public void ImagePromptsFallBackToTheSceneDescription()
    {
        string[] visuals = ["a map of the city", "the dome from inside"];

        ImagePromptBuilder.Parse("""{"prompts":["Illustrated map, warm light"]}""", visuals).ShouldBe(["Illustrated map, warm light", "the dome from inside"]);
        ImagePromptBuilder.Parse("not json", visuals).ShouldBe(visuals);
        ImagePromptBuilder.User("Byzantium «x»", visuals).ShouldContain("Channel: «Byzantium \"x\"»");
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Models;
using TMConcept.Desktop.Catalog;
using TMConcept.Desktop.Updates;
using TMConcept.Engines.Images;
using TMConcept.Providers.Catalog;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The repository's model catalog (0.1.26): untrusted input that may only add what the app already runs. Every check refuses the whole file
/// with one reason; the built-in catalog is the fallback and the floor; the kept copy is read again at start and the repository at most once
/// a day. No test reads GitHub (a fake server), and no test adds image or Ollama models to the process-wide lists (only cloud models, which
/// live in each harness's own catalog).
/// </summary>
public sealed class RemoteCatalogTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void AValidFileBecomesTypedEntries()
    {
        using var h = new DesktopHarness();

        var remote = Reader(h).Read(Json(Sample()), "0.1.26");

        remote.Date.ShouldBe("2026-11-18");
        var cloud = remote.Models.Single(m => m.Provider == "openai");
        cloud.Id.ShouldBe("gpt-test-remote");
        cloud.Capabilities.ShouldBe(ModelCapability.Chat | ModelCapability.Schema);
        var image = remote.Images.ShouldHaveSingleItem();
        image.Id.ShouldBe("z-image-remote-test");
        image.ExtraArguments.ShouldBeEmpty("a remote model never carries command-line options");
        image.Files.ShouldAllBe(f => f.Url.Host == "huggingface.co" && f.Url.AbsolutePath.Contains("/resolve/" + Commit + "/", StringComparison.Ordinal));
        image.Diffusion.Sha256.ShouldBe(Sha);
        remote.Models.Single(m => m.Provider == ModelProviders.Local).Requires.ShouldBe(["image.z-image-remote-test"]);
        remote.Ollama.ShouldHaveSingleItem().Tag.ShouldBe("qwen9.9:4b-q4_K_M");
        remote.Digests["qwen9.9:4b-q4_K_M"].ShouldBe("sha256:" + Sha);
        remote.Count.ShouldBe(3);
    }

    [Fact]
    public void AnImageFileWithoutItsSha256RefusesTheWholeFile()
    {
        using var h = new DesktopHarness();
        var file = Sample();
        file.Images![0].Files![0].Sha256 = string.Empty;

        Refused(h, file).ShouldContain("no SHA-256");

        file.Images[0].Files![0].Sha256 = Sha.ToUpperInvariant();
        Refused(h, file).ShouldContain("no SHA-256", Case.Sensitive, "lower-case hex only");
        file.Images[0].Files![0].Sha256 = Sha;
        file.Images[0].Files![0].Bytes = 0;
        Refused(h, file).ShouldContain("file size");
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("github.com")]
    [InlineData("HuggingFace.co")]
    [InlineData("")]
    public void AModelFileFromAnotherHostRefusesTheWholeFile(string host)
    {
        using var h = new DesktopHarness();
        var file = Sample();
        file.Images![0].Files![1].Host = host;

        Refused(h, file).ShouldContain("is not allowed for model files");
    }

    [Theory]
    [InlineData("comfyui")]
    [InlineData("SD-CPP")]
    [InlineData("")]
    public void AnUnknownEngineRefusesTheWholeFile(string engine)
    {
        using var h = new DesktopHarness();
        var file = Sample();
        file.Images![0].Engine = engine;

        Refused(h, file).ShouldContain("is not one the app has");
    }

    [Fact]
    public void OnlyWhatTheAppAlreadyRunsIsAccepted()
    {
        using var h = new DesktopHarness();

        Refused(h, Sample(f => f.Providers![0].Provider = "someone-new")).ShouldContain("not one the app knows");
        Refused(h, Sample(f => f.Providers![0].Provider = ModelProviders.Local)).ShouldContain("not one the app knows");
        Refused(h, Sample(f => f.Providers![0].Capabilities = "Chat, Teleport")).ShouldContain("capabilities");
        Refused(h, Sample(f => f.Providers![0].Capabilities = "3")).ShouldContain("capabilities", Case.Sensitive, "no numeric flags");
        Refused(h, Sample(f => f.Providers![0].Efforts = ["ludicrous"])).ShouldContain("effort");
        Refused(h, Sample(f => f.Images![0].Family = "sd3")).ShouldContain("family", Case.Sensitive, "SD 3.5 needs its extra sd-cli options");
        Refused(h, Sample(f => f.Images![0].Sampler = "my-own-sampler")).ShouldContain("sampler");
        Refused(h, Sample(f => f.Images![0].Files![0].Commit = "main")).ShouldContain("commit");
        Refused(h, Sample(f => f.Images![0].Files![0].Path = "../../outside.gguf")).ShouldContain("file path");
        Refused(h, Sample(f => f.Images![0].Files![1].Role = "Diffusion")).ShouldContain("exactly one diffusion model");
        Refused(h, Sample(f => f.Images![0].LicenseUse = "NonCommercial")).ShouldContain("licence address", Case.Sensitive,
            "a non-commercial model must say where its licence is");
        Refused(h, Sample(f => f.Ollama![0].Tag = "qwen9.9:latest")).ShouldContain("exact tag");
        Refused(h, Sample(f => f.Ollama![0].ManifestDigest = string.Empty)).ShouldContain("manifest digest");
        Refused(h, Sample(f => f.Ollama![0].SourceUrl = "https://example.org/qwen")).ShouldContain("library page");
        Refused(h, Sample(f => f.Providers!.Add(f.Providers[0]))).ShouldContain("listed twice");
    }

    [Fact]
    public void AFieldTheAppDoesNotKnowAHigherSchemaOrANewerMinimumRefusesTheWholeFile()
    {
        using var h = new DesktopHarness();
        var json = Json(Sample());

        Refused(h, json.Replace("\"engine\":", "\"extraArguments\": [\"--anything\"], \"engine\":", StringComparison.Ordinal))
            .ShouldContain("not a model catalog", Case.Sensitive, "free command-line options cannot be smuggled in");
        Refused(h, Json(Sample(f => f.Schema = 2))).ShouldContain("schema 2");
        Refused(h, Json(Sample(f => f.MinApp = "0.1.27"))).ShouldContain("needs TM Concept 0.1.27");
        Refused(h, Json(Sample(f => f.Date = "18.11.2026"))).ShouldContain("date");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"schema\": 1, ")]
    [InlineData("[]")]
    [InlineData("null")]
    public void MalformedJsonRefusesTheWholeFile(string json)
    {
        using var h = new DesktopHarness();

        Should.Throw<RemoteCatalogException>(() => Reader(h).Read(json, "0.1.26"));
    }

    [Theory]
    [InlineData("https://evil.example/collect")]
    [InlineData("fal-ai/../x")]
    [InlineData("fal-ai//x")]
    [InlineData("evil:collect")]
    [InlineData("fal-ai/./x")]
    public void ACloudModelIdThatIsAnAddressOrLeavesItsPathRefusesTheWholeFile(string id)
    {
        using var h = new DesktopHarness();

        Refused(h, Sample(f =>
        {
            f.Providers![0].Provider = "fal";
            f.Providers[0].Id = id;
            f.Providers[0].Capabilities = "Image";
        })).ShouldContain("the id");
        RemoteCatalogReader.SafeModelId("fal-ai/flux-3/dev").ShouldBeTrue();
        RemoteCatalogReader.SafeModelId("meta-llama/llama-5:free").ShouldBeTrue("OpenRouter's free variants keep their suffix");
    }

    [Theory]
    [InlineData("\"id\": null")]
    [InlineData("\"name\": null")]
    [InlineData("\"negativePrompt\": null")]
    [InlineData("\"files\": [null]")]
    [InlineData("\"providers\": [null]")]
    [InlineData("\"images\": [null]")]
    [InlineData("\"ollama\": [null]")]
    [InlineData("\"efforts\": [null]")]
    public void ANullValueOrNullItemRefusesTheWholeFileWithAReason(string field)
    {
        using var h = new DesktopHarness();
        var name = field[..field.IndexOf(':', StringComparison.Ordinal)];
        var json = JsonNode.Parse(Json(Sample()))!.AsObject();
        var value = JsonNode.Parse(field[(field.IndexOf(':', StringComparison.Ordinal) + 1)..]);
        var key = name.Trim('"');
        JsonObject target = key switch
        {
            "providers" or "images" or "ollama" => json,
            "efforts" => json["providers"]![0]!.AsObject(),
            _ => json["images"]![0]!.AsObject(),
        };
        target[key] = value;

        Should.Throw<RemoteCatalogException>(() => Reader(h).Read(json.ToJsonString(), "0.1.26"));
    }

    [Fact]
    public void AKeptCopyThisVersionCannotCheckNeverStopsTheStart()
    {
        using var h = new DesktopHarness();
        var updater = h.Get<RemoteCatalogUpdater>();
        Directory.CreateDirectory(updater.Folder);

        File.WriteAllText(Path.Combine(updater.Folder, "repository.json"), Json(Sample()).Replace("\"name\": \"GPT test\"", "\"name\": null", StringComparison.Ordinal));

        updater.ApplySaved().ShouldBeNull();
    }

    [Theory]
    [InlineData("z-image-turbo.")]
    [InlineData("z-image-turbo-")]
    [InlineData("a..b")]
    public void AnImageIdMustEndInALetterOrDigitSoItIsNeverABuiltInFolder(string id)
    {
        using var h = new DesktopHarness();

        Refused(h, Sample(f => f.Images![0].Id = id)).ShouldContain("the id");
        Refused(h, Sample(f => f.Images![0].Files![0].Path = "z.gguf.")).ShouldContain("file path", Case.Sensitive, "Windows drops the dot: two names, one file");
        Refused(h, Sample(f => f.Images![0].Files![1].Path = "vae./ae.safetensors")).ShouldContain("file path");
        Refused(h, Sample(f => f.Ollama![0].Tag = "qwen9.9:4b.")).ShouldContain("exact tag");
    }

    [Theory]
    [InlineData("FLUX 3 ‮(eerf)")]
    [InlineData("Free​ model")]
    [InlineData("Model ")]
    public void ANameWithInvisibleOrDirectionCharactersRefusesTheWholeFile(string name)
    {
        using var h = new DesktopHarness();

        Refused(h, Sample(f => f.Providers![0].Name = name)).ShouldContain("the name");
        Refused(h, Sample(f => f.Ollama![0].Name = name)).ShouldContain("the name");
        Reader(h).Read(Json(Sample(f => f.Providers![0].Name = "GPT ünlü — test")), "0.1.26").Models.ShouldContain(m => m.Name == "GPT ünlü — test");
    }

    [Fact]
    public void TheBuiltInCatalogIsTheFloorAnIdItHasIsNeverReplaced()
    {
        using var h = new DesktopHarness();
        var builtInImage = LocalImageModels.All[0].Id;
        var builtInCloud = h.Get<EmbeddedModelCatalog>().FileModels.First(m => m.Provider == "openai" && m.Capabilities.HasFlag(ModelCapability.Chat));
        var file = Sample(f =>
        {
            f.Images![0].Id = builtInImage;
            f.Providers![0].Id = builtInCloud.Id;
            f.Ollama![0].Tag = AppModelCatalog.Models[0].Tag;
        });

        var remote = Reader(h).Read(Json(file), "0.1.26");

        remote.Ignored.ShouldBe(3);
        remote.Count.ShouldBe(0);
        remote.Models.ShouldBeEmpty();
    }

    [Fact]
    public void AModelThatNeedsANewerStableDiffusionBuildIsLeftOutNotRefused()
    {
        using var h = new DesktopHarness();

        var remote = Reader(h).Read(Json(Sample(f => f.Images![0].MinSdCppBuild = RemoteCatalogRules.MaxSdCppBuild + 1)), "0.1.26");

        remote.Skipped.ShouldBe(1);
        remote.Images.ShouldBeEmpty();
        remote.Models.ShouldNotContain(m => m.Provider == ModelProviders.Local);
    }

    [Fact]
    public void TheRepositorysOwnCatalogFileIsValid()
    {
        using var h = new DesktopHarness();
        var json = File.ReadAllText(Path.Combine(TMConcept.Tests.Architecture.RepositoryFiles.Root, "catalog", "models.json"));

        var file = JsonSerializer.Deserialize(json, RemoteCatalogJsonContext.Default.RemoteCatalogFile)!;
        Reader(h).Read(json, file.MinApp).Ignored.ShouldBe(0, "the repository file only adds models");
        RemoteCatalogRules.RepositoryFile.ToString().ShouldBe("https://raw.githubusercontent.com/TM-AutoClip/tm-concept/main/catalog/models.json");
    }

    [Fact]
    public async Task TheRepositoryIsReadAtMostOnceADayAndTheKeptCopyIsAddedAtTheNextStart()
    {
        var server = new FakeCatalogServer(Json(CloudOnly("gpt-test-daily")));
        using var h = Harness(server, out var clock);
        var updater = h.Get<RemoteCatalogUpdater>();

        (await updater.UpdateAsync(force: false, CancellationToken.None)).ShouldBeTrue();
        server.Requests.ShouldHaveSingleItem().ShouldBe(RemoteCatalogRules.RepositoryFile);
        h.Get<EmbeddedModelCatalog>().Find("openai", "gpt-test-daily").ShouldBeNull("what the file adds appears at the next start");
        (await updater.UpdateAsync(force: false, CancellationToken.None)).ShouldBeFalse();
        server.Requests.Count.ShouldBe(1, "within a day the kept copy is used");
        clock.Advance(TimeSpan.FromDays(1.1));
        (await updater.UpdateAsync(force: false, CancellationToken.None)).ShouldBeTrue();
        server.Requests.Count.ShouldBe(2);

        // The next start: the kept copy is checked again and added before anything reads the lists.
        var applied = updater.ApplySaved().ShouldNotBeNull();
        applied.Count.ShouldBe(1);
        h.Get<EmbeddedModelCatalog>().Find("openai", "gpt-test-daily").ShouldNotBeNull();
        h.Get<IModelCatalog>().Find("openai", "gpt-test-daily").ShouldNotBeNull();
        h.Get<IModelCatalog>().BuiltIn("openai", "gpt-test-daily").ShouldNotBeNull("the prices file can price it like a built-in model");
    }

    [Fact]
    public async Task ABrokenOrUnreachableFileKeepsTheLastGoodCopy()
    {
        var server = new FakeCatalogServer(Json(CloudOnly("gpt-test-good")));
        using var h = Harness(server, out _);
        var updater = h.Get<RemoteCatalogUpdater>();
        var kept = Path.Combine(updater.Folder, "repository.json");
        (await updater.UpdateAsync(force: true, CancellationToken.None)).ShouldBeTrue();
        var good = File.ReadAllText(kept);

        server.Body = "{ \"schema\": 1, \"date\": \"2026-11-19\", \"minApp\": \"0.1.0\", \"images\": [ { \"engine\": \"comfyui\" } ] }";
        (await updater.UpdateAsync(force: true, CancellationToken.None)).ShouldBeTrue();
        File.ReadAllText(kept).ShouldBe(good, "a refused file changes nothing");

        server.Body = "<html>rate limited</html>";
        server.Status = HttpStatusCode.TooManyRequests;
        (await updater.UpdateAsync(force: true, CancellationToken.None)).ShouldBeFalse();
        File.ReadAllText(kept).ShouldBe(good);
        updater.ApplySaved()!.Models.Select(m => m.Id).ShouldBe(["gpt-test-good"]);
    }

    [Fact]
    public void ADamagedKeptCopyIsNotUsedAndTheBuiltInCatalogStays()
    {
        using var h = new DesktopHarness();
        var updater = h.Get<RemoteCatalogUpdater>();
        Directory.CreateDirectory(updater.Folder);
        var before = h.Get<EmbeddedModelCatalog>().All.Count;

        File.WriteAllText(Path.Combine(updater.Folder, "repository.json"), "{ damaged");
        updater.ApplySaved().ShouldBeNull();

        File.WriteAllText(Path.Combine(updater.Folder, "repository.json"), Json(Sample(f => f.Images![0].Engine = "comfyui")));
        updater.ApplySaved().ShouldBeNull();

        Directory.Delete(updater.Folder, recursive: true);
        updater.ApplySaved().ShouldBeNull("no copy yet: nothing to add");
        h.Get<EmbeddedModelCatalog>().All.Count.ShouldBe(before);
    }

    [Fact]
    public async Task AnInstalledModelALaterFileDropsStaysInTheKeptCopy()
    {
        var first = CloudOnly("gpt-test-a");
        first.Ollama = [Ollama("remote-only9:1b-q4_K_M"), Ollama("remote-only9:2b-q4_K_M")];
        var server = new FakeCatalogServer(Json(first));
        using var h = Harness(server, out _);
        var updater = h.Get<RemoteCatalogUpdater>();
        (await updater.UpdateAsync(force: true, CancellationToken.None)).ShouldBeTrue();

        // The 1b model was pulled (its manifest is on disk); the next file lists neither.
        var manifest = h.Get<AppModelStore>().ManifestPath("remote-only9:1b-q4_K_M");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, "{}");
        server.Body = Json(CloudOnly("gpt-test-b"));
        (await updater.UpdateAsync(force: true, CancellationToken.None)).ShouldBeTrue();

        var kept = Reader(h).Read(File.ReadAllText(Path.Combine(updater.Folder, "repository.json")), "0.1.26");
        kept.Ollama.Select(m => m.Tag).ShouldBe(["remote-only9:1b-q4_K_M"], "installed: kept usable and removable; the other one is gone");
        kept.Models.Select(m => m.Id).ShouldBe(["gpt-test-b"]);
    }

    private static RemoteCatalogReader Reader(DesktopHarness h) => h.Get<RemoteCatalogReader>();

    private static string Refused(DesktopHarness h, RemoteCatalogFile file) => Refused(h, Json(file));

    private static string Refused(DesktopHarness h, string json) => Should.Throw<RemoteCatalogException>(() => Reader(h).Read(json, "0.1.26")).Message;

    private static string Json(RemoteCatalogFile file) => JsonSerializer.Serialize(file, RemoteCatalogJsonContext.Default.RemoteCatalogFile);

    private static RemoteCatalogFile CloudOnly(string id)
    {
        var file = Sample();
        file.Providers![0].Id = id;
        file.Images = [];
        file.Ollama = [];
        file.MinApp = "0.1.0";
        return file;
    }

    private static RemoteCatalogFile Sample(Action<RemoteCatalogFile>? change = null)
    {
        var builtIn = LocalImageModels.All.First(m => m.Family == "z-image");
        var file = new RemoteCatalogFile
        {
            Schema = 1,
            Date = "2026-11-18",
            MinApp = "0.1.26",
            Providers =
            [
                new RemoteProviderModel
                {
                    Provider = "openai", Id = "gpt-test-remote", Name = "GPT test", Capabilities = "Chat, Schema", ContextTokens = 400_000,
                    MaxOutputTokens = 128_000, InputUsd = 1.25m, OutputUsd = 10m,
                },
            ],
            Images =
            [
                new RemoteImageModel
                {
                    Engine = "sd-cpp", Id = "z-image-remote-test", Name = "Z-Image test", Family = "z-image", Steps = 8, Sampler = builtIn.Sampler,
                    License = "apache-2.0", LicenseUse = "Commercial", Tier = 6,
                    Files =
                    [
                        new RemoteImageFile { Role = "Diffusion", Host = "huggingface.co", Repository = "someone/z-image-test", Commit = Commit, Path = "z.gguf", Sha256 = Sha, Bytes = 10 },
                        new RemoteImageFile { Role = "Vae", Host = "huggingface.co", Repository = "someone/z-image-test", Commit = Commit, Path = "vae/ae.safetensors", Sha256 = Sha, Bytes = 5 },
                    ],
                },
            ],
            Ollama = [Ollama("qwen9.9:4b-q4_K_M")],
        };
        change?.Invoke(file);
        return file;
    }

    private static RemoteOllamaModel Ollama(string tag) => new()
    {
        Tag = tag, Name = "Test " + tag, Family = tag.Split(':')[0], Bytes = 3_000_000_000, ContextTokens = 8_192, TrainedContextTokens = 262_144,
        KvBytesPerToken = 32_768, Vision = true, License = "apache-2.0", LicenseUse = "Commercial", SourceUrl = "https://ollama.com/library/qwen3.5/tags",
        ManifestDigest = "sha256:" + Sha,
    };

    private static DesktopHarness Harness(FakeCatalogServer server, out FakeTimeProvider clock)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        clock = time;
        return new DesktopHarness(s =>
        {
            s.AddSingleton(new UpdateTransport(() => server));
            s.AddSingleton<TimeProvider>(time);
        });
    }

    /// <summary>raw.githubusercontent.com: the file (or a status), recording what was asked.</summary>
    private sealed class FakeCatalogServer(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!);
            }

            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json"), RequestMessage = request });
        }
    }
}

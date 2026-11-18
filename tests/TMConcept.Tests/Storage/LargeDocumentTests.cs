using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Documents;
using TMConcept.Core.Keys;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;
using TMConcept.Core.Runs;
using TMConcept.Documents.Extractors;
using TMConcept.Storage.Stores;
using TMConcept.Tests.Documents;

namespace TMConcept.Tests.Storage;

/// <summary>
/// 0.1.24, large documents in the library: a page range is added as a document of its own (its title names the pages, the same range twice
/// is refused), scanned pages are read by a vision model (a fake one) whose calls are charged to a run, the estimates (price from the price
/// table, a rough time, calls), "read everything" (map-reduce) makes exactly the calls its plan said with an 8k local model, and a page range
/// of a run keeps the passages on its pages. Fake models only; nothing goes to a network.
/// </summary>
public sealed class LargeDocumentTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly CatalogModel Paid = new()
    {
        Provider = "anthropic", Id = "claude-haiku-5-5", Name = "Claude Haiku 5.5", Capabilities = ModelCapability.Chat | ModelCapability.Vision,
        InputUsd = 1m, OutputUsd = 5m, ContextTokens = 200_000, MaxOutputTokens = 64_000,
    };

    private static readonly CatalogModel Small = new()
    {
        Provider = ModelProviders.AppOllama, Id = "gemma-small", Name = "Gemma small", Capabilities = ModelCapability.Chat, ContextTokens = 8_192,
        MaxOutputTokens = 8_192, MinVideoMemoryGb = 6,
    };

    private static DocumentLibrary Library(StorageHarness h) =>
        new(h.Paths, new DocumentStore(h.Database), [new PdfExtractor(), new PlainTextExtractor()], [], new OffEmbedder(), TimeProvider.System,
            NullLogger<DocumentLibrary>.Instance);

    private static async Task<Profile> ProfileAsync(StorageHarness h)
    {
        var profile = new Profile { Name = "Byzantium", ContentLanguage = ContentLanguages.English };
        await new ProfileStore(h.Database).SaveAsync(profile, None);
        return profile;
    }

    [Fact]
    public async Task APageRangeIsADocumentOfItsOwnAndTheSameRangeIsNotAddedTwice()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var file = LargePdfTests.WritePdf(h.Paths, "volume.pdf", 30, _ => true);
        var library = Library(h);

        var probe = await library.ProbeAsync(file, None);
        probe.Pages.ShouldBe(30);
        var range = await library.AddFileAsync(profile.Id, file, new DocumentReadOptions(new DocumentPageRange(12, 18)), null, None);
        var whole = await library.AddFileAsync(profile.Id, file, null, None);

        (range.Document.Title, range.Document.Pages).ShouldBe(("volume (p. 12–18)", 7));
        (whole.Document.Title, whole.Document.Pages).ShouldBe(("volume", 30));
        range.Document.Sha256.ShouldNotBe(whole.Document.Sha256);
        var chunks = await new DocumentStore(h.Database).ChunksAsync(profile.Id, [range.Document.Id], None);
        chunks.ShouldAllBe(c => c.Page >= 12 && c.Page <= 18);
        var again = await Should.ThrowAsync<DocumentRejectedException>(() =>
            library.AddFileAsync(profile.Id, file, new DocumentReadOptions(new DocumentPageRange(12, 18)), null, None));
        again.Code.ShouldBe("duplicate");
    }

    [Fact]
    public async Task ScannedPagesAreReadByAVisionModelAndItsCallsAreChargedToARun()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var file = LargePdfTests.WritePdf(h.Paths, "scan.pdf", 5, page => page == 1);
        var chat = new EveryCallChat(r => "Konstantinopolis'in surları üç sıraydı. " + r.User);
        var runs = new RunStore(h.Database);
        var reader = new DocumentTextReader(chat, new CostCalculator(), runs, TimeProvider.System, NullLogger<DocumentTextReader>.Instance);
        var key = new ApiKey { Provider = "anthropic", Label = "mine", Secret = "fake-test-key" };

        var recognizer = await reader.StartAsync(profile.Id, new ResolvedModel(Paid, key, string.Empty), "scan.pdf", 4, None);
        var added = await Library(h).AddFileAsync(profile.Id, file, new DocumentReadOptions(Recognizer: recognizer), null, None);
        await reader.FinishAsync(recognizer, RunStatus.Succeeded, string.Empty);

        (added.PagesRecognized, added.PagesUnread, added.Document.PagesWithoutText).ShouldBe((4, 0, 0));
        chat.Requests.Count.ShouldBe(4);
        chat.Requests.ShouldAllBe(r => r.Images != null && r.Images.Count == 1 && r.System == VisionPageRecognizer.Instruction);
        chat.Requests.Select(r => r.User).ShouldBe(["Page 2.", "Page 3.", "Page 4.", "Page 5."]);
        var run = (await runs.ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).ShouldHaveSingleItem();
        (run.Kind, run.Status, run.Accepted, run.InputTokens, run.OutputTokens).ShouldBe((RunKind.DocumentText, RunStatus.Succeeded, 4, 4 * 1_700L, 4 * 600L));
        run.ActualUsd.ShouldBe(((4 * 1_700m * 1m) + (4 * 600m * 5m)) / 1_000_000m);
    }

    [Fact]
    public void TheEstimatesComeFromThePriceTableAndARoughSpeed()
    {
        var reader = new DocumentTextReader(new EveryCallChat(_ => string.Empty), new CostCalculator(), new NoRuns(), TimeProvider.System,
            NullLogger<DocumentTextReader>.Instance);

        var online = reader.Estimate(Paid, 700, 0);
        online.Calls.ShouldBe(700);
        online.Cost.InputTokens.ShouldBe(700L * (DocumentTextReader.ImageTokensPerPage + DocumentTextReader.InstructionTokens));
        online.Cost.MinUsd.ShouldBe(((online.Cost.InputTokens * 1m) + (700m * DocumentTextReader.PageOutputMin * 5m)) / 1_000_000m);
        online.Cost.MaxUsd.ShouldBe(((online.Cost.InputTokens * 1m) + (700m * DocumentTextReader.PageOutputMax * 5m)) / 1_000_000m);
        online.Local.ShouldBeFalse();
        online.TimeMin.ShouldBeGreaterThan(TimeSpan.FromMinutes(30));
        online.TimeMax.ShouldBeGreaterThan(online.TimeMin);
        reader.Estimate(Paid, 0, 0).ShouldBe(WorkEstimate.None);

        var card = 12L * 1024 * 1024 * 1024;
        var onCard = ModelSpeed.For(Small, card);
        var onProcessor = ModelSpeed.For(Small, 0);
        (onCard.Local, onCard.OnGraphicsCard, onProcessor.OnGraphicsCard).ShouldBe((true, true, false));
        onCard.OutputTokensPerSecond.ShouldBeGreaterThan(onProcessor.OutputTokensPerSecond * 5);
        var local = ModelSpeed.Estimate(Small, 0, 10, new CostEstimate(0, 0, 10_000, 1_000, 2_000));
        (local.Cost.MaxUsd, local.Local).ShouldBe((0m, true));
        local.TimeMin.ShouldBe(TimeSpan.FromSeconds(Math.Ceiling((10 * 2) + (10_000 / onProcessor.PromptTokensPerSecond) + (1_000 / onProcessor.OutputTokensPerSecond))));
    }

    [Fact]
    public void PackingFitsTheBudgetAndMergingStopsAtTheShare()
    {
        DocumentDigestPacker.Pack([100, 100, 100, 100, 100], 250).ShouldBe([(0, 2), (2, 2), (4, 1)]);
        DocumentDigestPacker.Pack([900, 10], 250).ShouldBe([(0, 1), (1, 1)], "a piece larger than a call is a call of its own");
        DocumentDigestPacker.Pack([], 250).ShouldBeEmpty();
        DocumentDigestPacker.MapCalls(100, 30_000, 3_000).ShouldBe(12);
        DocumentDigestPacker.Reduce(1, 3_000, 400).ShouldBe((0, 1, 0L));
        var (calls, notes, read) = DocumentDigestPacker.Reduce(40, 3_000, 2_000);
        (notes * DocumentDigestPacker.NoteTokens).ShouldBeLessThanOrEqualTo(2_000);
        calls.ShouldBe(6 + 1);
        read.ShouldBe(40 + 6);
    }

    [Fact]
    public async Task ReadingEverythingWithAn8kLocalModelMakesTheCallsItsPlanSaid()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var file = LargePdfTests.WritePdf(h.Paths, "volume.pdf", 3, _ => true);
        var text = Path.Combine(h.Paths.DataDirectory, "incoming", "long.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(text)!);
        await File.WriteAllTextAsync(text, string.Join("\n\n", Enumerable.Range(1, 120).Select(i =>
            $"Paragraph {i}: the merchants of the city paid their taxes in silver, and the emperor built walls, churches and harbours with it.")), None);
        var library = Library(h);
        await library.AddFileAsync(profile.Id, text, null, None);
        await library.AddFileAsync(profile.Id, file, null, None);
        var store = new DocumentStore(h.Database);
        var runs = new RunStore(h.Database);
        var chat = new EveryCallChat(r => r.System.Contains("merge", StringComparison.Ordinal)
            ? "Merged: " + new string('m', 1_432)
            : "Note: " + new string('n', 1_434));
        var grounder = new DocumentGrounder(store, new OffEmbedder(), new PassageRetriever([new Bm25Ranker()]), NullLogger<DocumentGrounder>.Instance);
        var digester = new DocumentDigester(store, grounder, chat, new CostCalculator(), runs, TimeProvider.System, NullLogger<DocumentDigester>.Instance);
        var key = new ApiKey { Provider = ModelProviders.AppOllama, Label = string.Empty, Secret = string.Empty };

        var plan = await digester.PlanAsync(profile, Small, [], null, 0, None);
        var steps = new List<DocumentDigestProgress>();
        var digest = await digester.RunAsync(profile, new ResolvedModel(Small, key, string.Empty), [], null, "walls", new LargePdfTests.Sync<DocumentDigestProgress>(steps.Add), None);

        plan.Documents.ShouldBe(2);
        plan.MapCalls.ShouldBeGreaterThan(1, "an 8k context reads the long file in several calls");
        plan.Estimate.Cost.MaxUsd.ShouldBe(0m, "a local model is free");
        plan.Estimate.Local.ShouldBeTrue();
        digest.Calls.ShouldBeInRange(plan.Calls - 2, plan.Calls + 2, "the plan counts from the stored lengths, the run from the passages themselves");
        chat.Requests.Count.ShouldBe(digest.Calls);
        steps.Count.ShouldBe(digest.Calls);
        chat.Requests.ShouldAllBe(r => r.User.Contains("LEAN TOWARDS: walls", StringComparison.Ordinal));
        var budget = DocumentDigester.Budgets(Small, 2).Budget;
        chat.Requests.ShouldAllBe(r => r.User.Length / 3.6 < budget + 100, "every call fits the small context");
        digest.Notes.ShouldNotBeEmpty();
        digest.Notes.Select(n => n.Ordinal).ShouldBe(Enumerable.Range(0, digest.Notes.Count));
        var run = (await runs.ListAsync(DateTimeOffset.UtcNow.AddHours(-1), None)).ShouldHaveSingleItem();
        (run.Kind, run.Status, run.Accepted).ShouldBe((RunKind.DocumentDigest, RunStatus.Succeeded, digest.Notes.Count));

        // The concepts are then grounded in the notes, not in the stored passages.
        var tokens = PassageTokens.For(new CostCalculator(), ContentLanguages.English);
        var grounding = await grounder.GroundAsync(profile, [], "walls", 3_000, false, tokens, None, digest: digest.Notes);
        grounding.Passages.ShouldNotBeEmpty();
        grounding.Passages.ShouldAllBe(p => p.Text.StartsWith("Note:", StringComparison.Ordinal) || p.Text.StartsWith("Merged:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APageRangeOfARunKeepsThePassagesOnItsPages()
    {
        using var h = await StorageHarness.CreatedAsync();
        var profile = await ProfileAsync(h);
        var file = LargePdfTests.WritePdf(h.Paths, "volume.pdf", 12, _ => true, lines: 12);
        await Library(h).AddFileAsync(profile.Id, file, null, None);
        var store = new DocumentStore(h.Database);
        var grounder = new DocumentGrounder(store, new OffEmbedder(), new PassageRetriever([new Bm25Ranker()]), NullLogger<DocumentGrounder>.Instance);
        var tokens = PassageTokens.For(new CostCalculator(), ContentLanguages.English);

        var some = await grounder.GroundAsync(profile, [], "walls", 6_000, false, tokens, None, pages: new DocumentPageRange(4, 6));
        some.Passages.ShouldNotBeEmpty();
        some.Passages.ShouldAllBe(p => p.Locator == "p. 4" || p.Locator == "p. 5" || p.Locator == "p. 6");
        var none = await Should.ThrowAsync<ProviderException>(() => grounder.GroundAsync(profile, [], "walls", 6_000, false, tokens, None,
            pages: new DocumentPageRange(40, 60)));
        none.Code.ShouldBe("pages");
        var all = await grounder.EstimateTokensAsync(profile, [], 1_000_000, None);
        var part = await grounder.EstimateTokensAsync(profile, [], 1_000_000, None, new DocumentPageRange(1, 3));
        part.ShouldBeLessThan(all);
        var brief = new DocumentBrief(string.Empty, 2, []) { Pages = new DocumentPageRange(1, 3) };
        brief.Pages.ShouldNotBeNull().Count.ShouldBe(3);
    }

    /// <summary>A model that answers every call with the text the test makes of it (1,700 tokens in, 600 out).</summary>
    private sealed class EveryCallChat(Func<ChatRequest, string> answer) : IChatClientFactory
    {
        public List<ChatRequest> Requests { get; } = [];

        public IChatClient Create(CatalogModel model, string apiKey) => new Client(this, model);

        private sealed class Client(EveryCallChat owner, CatalogModel model) : IChatClient
        {
            public CatalogModel Model { get; } = model;

            public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.Requests.Add(request);
                return Task.FromResult(new ChatResponse(owner.Answer(request), new ChatUsage(1_700, 0, 0, 600), null, "end_turn", "r"));
            }
        }

        private string Answer(ChatRequest request) => answer(request);
    }

    /// <summary>No meaning model.</summary>
    private sealed class OffEmbedder : DocumentEmbedderBase
    {
        public override string ModelId => "off";

        public override string ComponentCode => "embedding.off";

        public override bool Ready => false;

        protected override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not installed");
    }

    /// <summary>A run store for an estimate (never written).</summary>
    private sealed class NoRuns : IRunStore
    {
        public Task AddAsync(Run run, CancellationToken cancellationToken) => throw new InvalidOperationException("no run in an estimate");

        public Task<Run?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Run?>(null);

        public Task UpdateAsync(Run run, CancellationToken cancellationToken) => throw new InvalidOperationException("no run in an estimate");

        public Task<IReadOnlyList<Run>> ListAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Run>>([]);

        public Task<double> AverageConceptOutputTokensAsync(Guid profileId, CancellationToken cancellationToken) => Task.FromResult(0d);

        public Task<IReadOnlyList<Run>> ConceptEditsNotInVideosAsync(Guid conceptId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Run>>([]);
    }
}

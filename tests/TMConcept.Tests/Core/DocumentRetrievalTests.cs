using Shouldly;
using TMConcept.Core.Documents;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>The keyword and meaning rankings, their fusion, the spread without a query, the per-document cap and the token budget.</summary>
public sealed class DocumentRetrievalTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static DocumentChunk Chunk(Guid document, int ordinal, string text) => new() { DocumentId = document, Ordinal = ordinal, Text = text };

    private static DocumentSearch Search(string query, IReadOnlyList<DocumentChunk> chunks, int budget = 10_000, Dictionary<Guid, float[]>? vectors = null,
        float[]? queryVector = null) => new(query, chunks, vectors ?? [], queryVector, budget);

    private static long Tokens(string text) => text.Length / 4;

    [Fact]
    public void Bm25PutsThePassageWithTheRareQueryWordFirstAndFoldsTurkish()
    {
        List<DocumentChunk> chunks =
        [
            Chunk(A, 0, "Konstantinopolis surları uzun yıllar dayandı."),
            Chunk(A, 1, "Rum ateşi denizde düşman gemilerini yaktı; RUM ATEŞİ bir sırdı."),
            Chunk(A, 2, "Surlar ve kapılar şehrin çevresini sardı."),
        ];

        var ranked = new Bm25Ranker().Rank(Search("rum atesi", chunks));

        ranked[0].Chunk.ShouldBe(chunks[1], "case and Turkish letters do not matter");
        ranked.ShouldAllBe(r => r.Score > 0);
        ranked.Count.ShouldBe(1, "only passages with a query word are ranked");
        new Bm25Ranker().Rank(Search(string.Empty, chunks)).ShouldBeEmpty("no query, no ranking");
    }

    [Fact]
    public void CosineRanksByTheStoredVectorsAndNeedsThem()
    {
        List<DocumentChunk> chunks = [Chunk(A, 0, "one"), Chunk(A, 1, "two"), Chunk(A, 2, "three")];
        var vectors = new Dictionary<Guid, float[]>
        {
            [chunks[0].Id] = [1, 0],
            [chunks[1].Id] = [0.6f, 0.8f],
            [chunks[2].Id] = [0, 1],
        };

        var ranked = new CosineRanker().Rank(Search("q", chunks, vectors: vectors, queryVector: [0, 1]));

        ranked.Select(r => r.Chunk).ShouldBe([chunks[2], chunks[1]], "the closest first; an orthogonal one scores 0 and is dropped");
        new CosineRanker().Rank(Search("q", chunks)).ShouldBeEmpty("without vectors the meaning search does not run");
    }

    [Fact]
    public void FusionPrefersAPassageBothRankingsLike()
    {
        List<DocumentChunk> chunks =
        [
            Chunk(A, 0, "silk silk silk trade"),
            Chunk(A, 1, "silk caravans crossed the desert"),
            Chunk(A, 2, "the desert winds"),
        ];
        var vectors = new Dictionary<Guid, float[]> { [chunks[0].Id] = [0, 1], [chunks[1].Id] = [1, 0], [chunks[2].Id] = [0.9f, 0.1f] };
        var retriever = new PassageRetriever([new Bm25Ranker(), new CosineRanker()]);

        var fused = retriever.Fused(Search("silk", chunks, vectors: vectors, queryVector: [1, 0]));

        fused[0].ShouldBe(chunks[1], "second by words, first by meaning beats first by words only");
        fused.ShouldContain(chunks[2], "a passage only the meaning search finds still comes");
    }

    [Fact]
    public void WithoutAQueryThePassagesAreSpreadOverEachDocumentAndTheDocumentsTakeTurns()
    {
        var chunks = Enumerable.Range(0, 8).Select(i => Chunk(A, i, "a" + i)).Concat(Enumerable.Range(0, 2).Select(i => Chunk(B, i, "b" + i))).ToList();

        var spread = PassageRetriever.Spread(chunks);

        spread.Select(c => c.Text).Take(6).ShouldBe(["a0", "b0", "a4", "b1", "a2", "a6"]);
        spread.Count.ShouldBe(chunks.Count);
    }

    [Fact]
    public void ThePassagesFitTheBudgetOneDocumentDoesNotTakeAllAndTheyAreSentInOrder()
    {
        var chunks = Enumerable.Range(0, 20).Select(i => Chunk(A, i, "silk " + new string('x', 396))).Append(Chunk(B, 0, "silk in Venice " + new string('y', 385))).ToList();
        SourceDocument[] documents = [new() { Id = A, Title = "Big" }, new() { Id = B, Title = "Small" }];
        var retriever = new PassageRetriever([new Bm25Ranker()]);

        var grounding = retriever.Select(Search("silk", chunks, budget: 1_000), documents, Tokens);

        grounding.Passages.Sum(p => Tokens(p.Text) + 12).ShouldBeLessThanOrEqualTo(1_000);
        grounding.Documents.Select(d => d.Title).ShouldBe(["Big", "Small"], ignoreOrder: true);
        grounding.Passages.ShouldContain(p => p.Document == 2, "the small document is not crowded out");
        grounding.Passages.Select(p => p.Document).ShouldBeInOrder(SortDirection.Ascending);

        var wide = retriever.Select(Search("silk", chunks, budget: 100_000), documents, Tokens);
        wide.Passages.Count(p => p.Document == 1).ShouldBe(20, "with room to spare the cap is lifted");
    }

    [Fact]
    public void NoMatchFallsBackToTheSpread()
    {
        var chunks = Enumerable.Range(0, 4).Select(i => Chunk(A, i, "word" + i)).ToList();
        var grounding = new PassageRetriever([new Bm25Ranker()]).Select(Search("nothing matches", chunks), [new SourceDocument { Id = A, Title = "T" }], Tokens);
        grounding.Passages.Count.ShouldBe(4);
    }

    [Fact]
    public void TheBudgetFollowsTheContextAndThePrice()
    {
        var local8k = new CatalogModel { Id = "small", ContextTokens = 8_192, MaxOutputTokens = 4_096 };
        var local16k = new CatalogModel { Id = "mid", ContextTokens = 16_384, MaxOutputTokens = 8_192 };
        var paid = new CatalogModel { Id = "big", ContextTokens = 200_000, MaxOutputTokens = 64_000, InputUsd = 3, OutputUsd = 15 };
        var tiny = new CatalogModel { Id = "tiny", ContextTokens = 4_096, MaxOutputTokens = 2_048 };

        DocumentBudget.LoweredOutput(local8k, 2_500, 1_500, 4_096).ShouldBe(1_500, "8k: the answer limit comes down to the batch's own need");
        var small = DocumentBudget.PassageTokens(local8k, 2_500, 1_500);
        small.ShouldBeInRange(3_000, 4_000);
        DocumentBudget.LoweredOutput(local16k, 2_500, 1_500, 8_192).ShouldBeNull();
        DocumentBudget.PassageTokens(local16k, 2_500, 8_192).ShouldBeInRange(DocumentLimits.PassageTokensMin, 6_000);
        DocumentBudget.LoweredOutput(paid, 3_000, 6_000, 64_000).ShouldBeNull();
        DocumentBudget.PassageTokens(paid, 3_000, 64_000).ShouldBe(DocumentLimits.PassageTokensOnlineMax, "a paid model is capped");
        DocumentBudget.PassageTokens(tiny, 3_000, 900).ShouldBe(0, "too small: the run is refused with the reason");
        DocumentBudget.BatchSize(local8k, 20, 1_500).ShouldBe(2);
        DocumentBudget.BatchSize(paid, 20, 1_500).ShouldBe(20);
        DocumentBudget.BatchSize(tiny, 20, 5_000).ShouldBe(1, "never less than one");
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Documents;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Providers;

namespace TMConcept.Tests.Core;

/// <summary>The three concept sources: each takes only its own brief, and the documents source fits its passages into the model.</summary>
public sealed class ConceptSourceTests
{
    private static readonly CatalogModel Paid = new() { Provider = "anthropic", Id = "big", ContextTokens = 200_000, MaxOutputTokens = 64_000, InputUsd = 3, OutputUsd = 15 };

    private static readonly Profile Channel = new() { Name = "Byzantium", ContentLanguage = ContentLanguages.English };

    [Fact]
    public void EachSourceTakesOnlyItsOwnBrief()
    {
        var store = new MemoryDocumentStore();
        IConceptSource[] sources = [new TopicConceptSource(), new IdeaConceptSource(), Documents(store)];
        ConceptBrief[] briefs = [new TopicBrief(["Greek fire"]), new IdeaBrief("x", 2), new DocumentBrief(string.Empty, 2, [])];

        for (var i = 0; i < sources.Length; i++)
        {
            for (var j = 0; j < briefs.Length; j++)
            {
                sources[i].Handles(briefs[j]).ShouldBe(i == j, $"{sources[i].Kind} and {briefs[j].GetType().Name}");
            }
        }

        sources.Select(s => s.Kind).ShouldBe([ConceptSourceKind.Topics, ConceptSourceKind.Idea, ConceptSourceKind.Documents]);
    }

    [Fact]
    public async Task TopicAndIdeaSourcesPassTheirBriefOnUnchanged()
    {
        var topics = await new TopicConceptSource().PrepareAsync(Request(new TopicBrief(["A", "B", "C"]), 1, 2), CancellationToken.None);
        (topics.ExtraPrompt, topics.Grounding, topics.MaxOutputTokens).ShouldBe((string.Empty, null, null));
        topics.Topics.ShouldBe(["B", "C"]);

        var idea = await new IdeaConceptSource().PrepareAsync(Request(new IdeaBrief(" The walls ", 3), 0, 3), CancellationToken.None);
        idea.ExtraPrompt.ShouldBe("The walls");
        idea.Topics.ShouldBeNull();
        await Should.ThrowAsync<ArgumentException>(() => new IdeaConceptSource().PrepareAsync(Request(new TopicBrief(["A"]), 0, 1), CancellationToken.None));
    }

    [Fact]
    public async Task TheDocumentsSourceGroundsEachTopicInItsOwnPassages()
    {
        var store = new MemoryDocumentStore();
        var trade = store.Add(Channel.Id, "Trade", "Silk came by caravan to Constantinople.", "The emperor taxed the silk merchants.");
        store.Add(Channel.Id, "Walls", "The Theodosian walls had three lines.", "The walls held for a thousand years.");
        var source = Documents(store);
        var brief = new DocumentBrief("history", 0, [], ["the walls", "silk caravans"]);

        source.BatchSize(brief, Paid, 20, 1_000).ShouldBe(1, "with topics every concept is its own call");
        var first = await source.PrepareAsync(Request(brief, 0, 1), CancellationToken.None);
        var second = await source.PrepareAsync(Request(brief, 1, 1), CancellationToken.None);

        first.Topics.ShouldBe(["the walls"]);
        first.Grounding.ShouldNotBeNull().Passages[0].Text.ShouldContain("walls");
        second.Grounding.ShouldNotBeNull().Passages[0].Text.ShouldContain("Silk");
        second.Grounding.Documents[0].Id.ShouldBe(trade.Id);
        first.ExtraPrompt.ShouldBe("history");
    }

    [Fact]
    public async Task TheDocumentsSourceRefusesWithoutRoomOrWithoutDocuments()
    {
        var store = new MemoryDocumentStore();
        var source = Documents(store);
        var empty = await Should.ThrowAsync<ProviderException>(() => source.PrepareAsync(Request(new DocumentBrief(string.Empty, 1, []), 0, 1), CancellationToken.None));
        empty.Code.ShouldBe("documents");

        store.Add(Channel.Id, "Trade", "Silk.");
        var tiny = new CatalogModel { Id = "tiny", ContextTokens = 4_096, MaxOutputTokens = 2_048 };
        var request = new ConceptSourceRequest(Channel, new DocumentBrief(string.Empty, 1, []), tiny, 0, 1, 3_000, 900, 2_048, false);
        (await Should.ThrowAsync<ProviderException>(() => source.PrepareAsync(request, CancellationToken.None))).Code.ShouldBe("context");
    }

    [Fact]
    public async Task ASmallLocalContextGetsALowerAnswerLimit()
    {
        var store = new MemoryDocumentStore();
        store.Add(Channel.Id, "Trade", "Silk came by caravan.");
        var local = new CatalogModel { Provider = "ollama", Id = "small", ContextTokens = 8_192, MaxOutputTokens = 4_096 };
        var request = new ConceptSourceRequest(Channel, new DocumentBrief(string.Empty, 1, []), local, 0, 1, 2_500, 1_500, 4_096, false);

        var batch = await Documents(store).PrepareAsync(request, CancellationToken.None);

        batch.MaxOutputTokens.ShouldBe(1_500);
        batch.Grounding.ShouldNotBeNull().Passages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ATopicFoundInADocumentIsSearchedThereWithItsOwnWords()
    {
        var store = new MemoryDocumentStore();
        var walls = store.Add(Channel.Id, "Walls", "The Theodosian walls had three lines.", "The moat was wide.");
        var trade = store.Add(Channel.Id, "Trade", "Silk came by caravan to Constantinople.", "The emperor taxed the silk merchants.");
        var found = new DocumentTopic("İpek kervanları", ["Trade"]) { DocumentIds = [trade.Id], Keywords = ["caravan", "silk"] };
        var brief = new DocumentBrief(string.Empty, 0, [], [found]);

        var batch = await Documents(store).PrepareAsync(Request(brief, 0, 1), CancellationToken.None);

        batch.Topics.ShouldBe(["İpek kervanları"], "the topic is sent as the user saw it");
        var grounding = batch.Grounding.ShouldNotBeNull();
        grounding.Documents.ShouldHaveSingleItem().Id.ShouldBe(trade.Id, "a Turkish topic finds its English passages in its own document");
        grounding.Passages[0].Text.ShouldContain("caravan");
        brief.Topic(0).ShouldBe(found);

        var misnumbered = new DocumentBrief(string.Empty, 0, [], [found with { DocumentIds = [walls.Id] }]);
        var searched = await Documents(store).PrepareAsync(Request(misnumbered, 0, 1), CancellationToken.None);
        searched.Grounding.ShouldNotBeNull().Passages[0].Text.ShouldContain("caravan", Case.Sensitive, "nothing about it in the cited document: all are searched");

        store.Documents.Remove(trade);
        var fallback = await Documents(store).PrepareAsync(Request(brief, 0, 1), CancellationToken.None);
        fallback.Grounding.ShouldNotBeNull().Documents.ShouldHaveSingleItem().Id.ShouldBe(walls.Id, "its document is gone: the chosen ones are searched");
    }

    [Fact]
    public async Task ALaterBatchOfTheRunGetsOtherPassages()
    {
        var store = new MemoryDocumentStore();
        store.Add(Channel.Id, "Long", [.. Enumerable.Range(0, 120).Select(i => $"Chapter {i}: " + new string('x', 1_000))]);
        var source = Documents(store);
        var brief = new DocumentBrief(string.Empty, 4, []);

        var first = (await source.PrepareAsync(Request(brief, 0, 2), CancellationToken.None)).Grounding.ShouldNotBeNull();
        var used = first.Passages.Select(p => p.ChunkId).ToHashSet();
        var second = (await source.PrepareAsync(Request(brief, 2, 2) with { Used = used }, CancellationToken.None)).Grounding.ShouldNotBeNull();

        first.Passages.Count.ShouldBeLessThan(120, "the paid budget holds only part of the document");
        second.Passages.Select(p => p.ChunkId).ShouldNotContain(id => used.Contains(id), "the second batch reads further");
    }

    [Fact]
    public async Task ABatchThatLeavesNoRoomForPassagesGetsSmallerInsteadOfBeingRefused()
    {
        var store = new MemoryDocumentStore();
        store.Add(Channel.Id, "Trade", "Silk came by caravan.");
        var local = new CatalogModel { Provider = "ollama", Id = "small", ContextTokens = 8_192, MaxOutputTokens = 4_096 };

        // A bare prompt of 3,500 tokens (history and wishes): three concepts of 900 tokens leave too little room, one leaves enough.
        var request = new ConceptSourceRequest(Channel, new DocumentBrief(string.Empty, 3, []), local, 0, 3, 3_500, 2_700, 4_096, false);
        var batch = await Documents(store).PrepareAsync(request, CancellationToken.None);

        batch.Size.ShouldNotBeNull().ShouldBeInRange(1, 2);
        batch.Grounding.ShouldNotBeNull().Passages.ShouldHaveSingleItem();
        DocumentBudget.PassageTokens(local, 3_500, batch.MaxOutputTokens ?? 4_096).ShouldBeGreaterThanOrEqualTo(DocumentLimits.PassageTokensMin);
        (await Documents(store).PrepareAsync(Request(new DocumentBrief(string.Empty, 2, []), 0, 2), CancellationToken.None)).Size.ShouldBeNull("room enough: as asked");
    }

    [Fact]
    public async Task AnEstimateReadsNoPassageAndChargesTheBudget()
    {
        var store = new MemoryDocumentStore();
        store.Add(Channel.Id, "Long", [.. Enumerable.Range(0, 200).Select(i => new string('x', 1_200))]);
        store.Add(Channel.Id, "Short", "Silk.");

        var estimate = await Documents(store).PrepareAsync(Request(new DocumentBrief(string.Empty, 2, []), 0, 2) with { Estimate = true }, CancellationToken.None);

        store.ChunkReads.ShouldBe(0, "typing in the subject box must not decrypt every passage");
        estimate.EstimatedTokens.ShouldBe(DocumentLimits.PassageTokensOnlineMax, "a long document fills the paid budget");
        estimate.Grounding.ShouldNotBeNull().Passages.ShouldBeEmpty();

        var small = new MemoryDocumentStore();
        var shortOne = small.Add(Channel.Id, "Short", "Silk came by caravan.");
        var few = await Documents(small).PrepareAsync(Request(new DocumentBrief(string.Empty, 1, [shortOne.Id]), 0, 1) with { Estimate = true }, CancellationToken.None);
        few.EstimatedTokens.ShouldBeInRange(1, 100, "a short document costs what it holds");
    }

    private static ConceptSourceRequest Request(ConceptBrief brief, int offset, int size) =>
        new(Channel, brief, Paid, offset, size, 3_000, size * 1_500L, 64_000, false);

    private static DocumentConceptSource Documents(MemoryDocumentStore store) =>
        new(new DocumentGrounder(store, new OffEmbedder(), new PassageRetriever([new Bm25Ranker(), new CosineRanker()]), NullLogger<DocumentGrounder>.Instance),
            new CostCalculator());

    /// <summary>No meaning model installed: the keyword search alone.</summary>
    private sealed class OffEmbedder : DocumentEmbedderBase
    {
        public override string ModelId => "off";

        public override string ComponentCode => "embedding.off";

        public override bool Ready => false;

        protected override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not installed");
    }
}

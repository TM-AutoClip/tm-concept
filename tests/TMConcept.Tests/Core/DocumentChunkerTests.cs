using Shouldly;
using TMConcept.Core.Documents;

namespace TMConcept.Tests.Core;

/// <summary>Cutting a document's text into passages: sizes, overlap, headings, pages, cleaning.</summary>
public sealed class DocumentChunkerTests
{
    private static string Sentence(int i) => $"Sentence number {i} tells how the caravans crossed the desert to the harbour of Antioch. ";

    [Fact]
    public void PassagesStayWithinTheLimitsAndOverlap()
    {
        var paragraphs = Enumerable.Range(0, 40).Select(i => string.Concat(Enumerable.Range(i * 3, 3).Select(Sentence)).Trim());
        var document = new ExtractedDocument("Trade", [new ExtractedSection(1, string.Empty, string.Join("\n\n", paragraphs))]);

        var chunks = DocumentChunker.Chunk(document);

        chunks.Count.ShouldBeGreaterThan(5);
        chunks.ShouldAllBe(c => c.Text.Length <= DocumentLimits.ChunkMaxCharacters);
        chunks.Select(c => c.Ordinal).ShouldBe(Enumerable.Range(0, chunks.Count));
        for (var i = 1; i < chunks.Count; i++)
        {
            var tail = chunks[i - 1].Text[^40..];
            chunks[i].Text.ShouldContain(tail, Case.Sensitive, "the next passage repeats the end of the one before");
        }
    }

    [Fact]
    public void ALongParagraphIsCutAtASentenceEndAndAHeadingStartsANewPassage()
    {
        var longParagraph = string.Concat(Enumerable.Range(0, 60).Select(Sentence));
        var document = new ExtractedDocument(string.Empty,
        [
            new ExtractedSection(0, "Silk", longParagraph),
            new ExtractedSection(0, "Silk › Spices", "Pepper came from India."),
        ]);

        var chunks = DocumentChunker.Chunk(document);

        chunks.ShouldAllBe(c => c.Text.Length <= DocumentLimits.ChunkMaxCharacters);
        chunks.Where(c => c.Heading == "Silk").ShouldAllBe(c => c.Text.TrimEnd().EndsWith('.'));
        chunks[^1].Heading.ShouldBe("Silk › Spices");
        chunks[^1].Text.ShouldBe("Pepper came from India.", "a new heading starts its own passage without the overlap of another section");
    }

    [Fact]
    public void APassageKeepsThePageItStartsOn()
    {
        var document = new ExtractedDocument(string.Empty,
            Enumerable.Range(1, 6).Select(p => new ExtractedSection(p, string.Empty, string.Concat(Enumerable.Range(0, 5).Select(Sentence)))).ToList(), 6);

        var chunks = DocumentChunker.Chunk(document);

        chunks[0].Page.ShouldBe(1);
        chunks.Select(c => c.Page).ShouldBeInOrder(SortDirection.Ascending);
        chunks[^1].Page.ShouldBeGreaterThan(3);
    }

    [Fact]
    public void EmptyTextGivesNoPassage() => DocumentChunker.Chunk(new ExtractedDocument("x", [])).ShouldBeEmpty();

    [Fact]
    public void CleaningJoinsHyphenatedWordsAndDropsRepeatedHeaders()
    {
        var pages = Enumerable.Range(1, 4).Select(p => new ExtractedSection(p, string.Empty,
            $"THE SILK ROAD — A HISTORY\nThe mer-\nchants of page {p} traded silk.\nPage {p}")).ToList();

        var clean = DocumentExtractorBase.Clean(new ExtractedDocument("t", pages, 4));

        clean.Sections.ShouldAllBe(s => !s.Text.Contains("THE SILK ROAD", StringComparison.Ordinal), "a running header on every page is left out");
        clean.Sections.ShouldAllBe(s => !s.Text.Contains("Page ", StringComparison.Ordinal), "page numbers that repeat with another digit are left out");
        clean.Sections[0].Text.ShouldContain("merchants of page 1 traded silk.");
    }
}

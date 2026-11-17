using TMConcept.Core.Documents;

namespace TMConcept.Documents.Extractors;

/// <summary>A plain text file (.txt): its text as one piece, read as UTF-8 or UTF-16 (by its byte order mark), otherwise Latin-1.</summary>
public sealed class PlainTextExtractor : DocumentExtractorBase
{
    /// <inheritdoc />
    public override DocumentKind Kind => DocumentKind.Text;

    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".txt"];

    /// <inheritdoc />
    protected override ExtractedDocument Read(string file, CancellationToken cancellationToken) =>
        new(string.Empty, [new ExtractedSection(0, string.Empty, TextFiles.Read(file))]);
}

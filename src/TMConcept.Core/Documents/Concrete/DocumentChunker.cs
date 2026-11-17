using System.Text;

namespace TMConcept.Core.Documents;

/// <summary>
/// Cuts a document's text into passages for the search (version <see cref="DocumentLimits.ChunkerVersion"/>): paragraphs are put together
/// up to <see cref="DocumentLimits.ChunkTargetCharacters"/>, never past <see cref="DocumentLimits.ChunkMaxCharacters"/> (a longer paragraph is
/// cut at a sentence end, otherwise at a space); a new heading starts a new passage; pages run on (a passage keeps the page it starts on);
/// the next passage of the same heading repeats the last <see cref="DocumentLimits.ChunkOverlapCharacters"/> or so (from a word start), so a
/// fact cut in two stays whole in one of them.
/// </summary>
public static class DocumentChunker
{
    /// <summary>The passages of <paramref name="document"/> (their ids are new; the caller sets the document and the profile).</summary>
    public static IReadOnlyList<DocumentChunk> Chunk(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var chunks = new List<DocumentChunk>();
        var builder = new StringBuilder();
        var heading = string.Empty;
        var page = 0;
        var started = false;

        void Flush(bool keepOverlap)
        {
            var text = builder.ToString().Trim();
            builder.Clear();
            if (text.Length == 0)
            {
                return;
            }

            chunks.Add(new DocumentChunk { Ordinal = chunks.Count, Page = page, Heading = heading, Text = text });
            if (keepOverlap)
            {
                builder.Append(Overlap(text)).Append('\n');
            }

            started = false;
        }

        foreach (var section in document.Sections)
        {
            if (!string.Equals(section.Heading, heading, StringComparison.Ordinal))
            {
                Flush(keepOverlap: false);
                heading = section.Heading;
            }

            foreach (var paragraph in Paragraphs(section.Text))
            {
                if (!started)
                {
                    page = section.Page;
                    started = true;
                }

                if (builder.Length > 0 && builder.Length + paragraph.Length + 1 > DocumentLimits.ChunkTargetCharacters)
                {
                    Flush(keepOverlap: true);
                    page = section.Page;
                    started = true;
                }

                builder.Append(paragraph).Append('\n');
            }
        }

        Flush(keepOverlap: false);
        return chunks;
    }

    /// <summary>The paragraphs of a text, each at most <see cref="DocumentLimits.ChunkMaxCharacters"/> - the overlap long.</summary>
    public static IEnumerable<string> Paragraphs(string text)
    {
        var limit = DocumentLimits.ChunkMaxCharacters - DocumentLimits.ChunkOverlapCharacters - 1;
        foreach (var raw in (text ?? string.Empty).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var rest = raw;
            while (rest.Length > limit)
            {
                var cut = Cut(rest, limit);
                yield return rest[..cut].Trim();
                rest = rest[cut..].Trim();
            }

            if (rest.Length > 0)
            {
                yield return rest;
            }
        }
    }

    /// <summary>Where to cut <paramref name="text"/> at most <paramref name="limit"/> long: after the last sentence end past the middle, else the last space, else the limit.</summary>
    private static int Cut(string text, int limit)
    {
        for (var i = limit - 1; i > limit / 2; i--)
        {
            if (text[i] is '.' or '!' or '?' or '…' && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                return i + 1;
            }
        }

        var space = text.LastIndexOf(' ', limit - 1);
        return space > limit / 2 ? space + 1 : limit;
    }

    /// <summary>About the last <see cref="DocumentLimits.ChunkOverlapCharacters"/> of a passage, from a word start.</summary>
    private static string Overlap(string text)
    {
        if (text.Length <= DocumentLimits.ChunkOverlapCharacters)
        {
            return text;
        }

        var from = text.Length - DocumentLimits.ChunkOverlapCharacters;
        var space = text.IndexOfAny([' ', '\n'], from);
        return space < 0 ? text[from..] : text[(space + 1)..];
    }
}

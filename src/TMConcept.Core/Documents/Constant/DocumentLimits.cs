namespace TMConcept.Core.Documents;

/// <summary>Sizes and limits of "Belgelerim" (documents → concepts, 0.1.21).</summary>
public static class DocumentLimits
{
    /// <summary>
    /// Largest Word, text or Markdown file that can be added (bigger ones are refused before anything is copied): these are read into memory
    /// whole, and 50 MB of plain text is already about 25,000 pages.
    /// </summary>
    public const long FileBytesMax = 50L * 1024 * 1024;

    /// <summary>
    /// Largest PDF that can be added (0.1.24; was 50 MB). A PDF is read page by page from the file (never loaded whole), so its size only
    /// costs disk space: the app keeps its own copy and hashes it. The cap stops a wrong pick (a disk image renamed .pdf) from filling the disk.
    /// </summary>
    public const long PdfBytesMax = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// A PDF is opened again after this many pages while it is read (0.1.24): the PDF reader keeps what it parsed, so a 700-page volume read in
    /// one go would hold every page's objects; reopening keeps the memory of a read about the same whatever the page count.
    /// </summary>
    public const int PdfReopenPages = 64;

    /// <summary>Pages looked at to tell whether a PDF is a scan before it is added (spread over the document; the count is shown).</summary>
    public const int ProbePages = 12;

    /// <summary>Most images of one scanned page sent to a text-extraction model (the largest ones; small icons and masks are left out).</summary>
    public const int PageImagesMax = 4;

    /// <summary>A page image smaller than this many pixels on a side is an icon or a rule, not text.</summary>
    public const int PageImagePixelsMin = 64;

    /// <summary>A page image larger than this is not sent (online services refuse bigger images, about 5 MB); the page counts as unread.</summary>
    public const int PageImageBytesMax = 5 * 1024 * 1024;

    /// <summary>Text-extraction calls that may fail in a row before the add stops (a wrong key or an empty balance fails every page).</summary>
    public const int RecognitionFailuresInARowMax = 3;

    /// <summary>Largest web page or article body read from the network.</summary>
    public const long WebBytesMax = 5L * 1024 * 1024;

    /// <summary>Longest wait for one web page or article (seconds).</summary>
    public const int WebSecondsMax = 30;

    /// <summary>
    /// Most characters of text kept from one document (0.1.24: 20 million, about 10,000 dense pages; was 3 million); the rest is left out
    /// and the document says so. The text is held in memory while it is cut into passages (about 40 MB at the cap), so a cap stays.
    /// </summary>
    public const int CharactersMax = 20_000_000;

    /// <summary>Longest stored title.</summary>
    public const int TitleMax = 200;

    /// <summary>A passage aims at this many characters (about 300–400 tokens: the meaning model reads 512 at most).</summary>
    public const int ChunkTargetCharacters = 1_200;

    /// <summary>A passage is never longer than this (a longer paragraph is cut at a sentence end or a space).</summary>
    public const int ChunkMaxCharacters = 1_600;

    /// <summary>The end of a passage that the next one repeats, so a fact cut in two is still whole in one of them.</summary>
    public const int ChunkOverlapCharacters = 200;

    /// <summary>The version of the cutting rules (a document cut by an older version can be cut again).</summary>
    public const int ChunkerVersion = 1;

    /// <summary>Fewest passage tokens a call must have room for; with less the run is refused with the reason (the model's context is too small).</summary>
    public const int PassageTokensMin = 1_200;

    /// <summary>Most passage tokens one call of a paid (online) model gets: a large context costs money.</summary>
    public const int PassageTokensOnlineMax = 12_000;

    /// <summary>The share of the context kept free as a margin (token counts are estimates).</summary>
    public const double ContextMargin = 0.10;

    /// <summary>Most passages taken from one document while other selected documents still have relevant ones.</summary>
    public const int PassagesPerDocumentMax = 6;

    /// <summary>The topics one "Find topics" asks for, at least.</summary>
    public const int TopicCountMin = 1;

    /// <summary>The topics one "Find topics" asks for, at most.</summary>
    public const int TopicCountMax = 30;

    /// <summary>The concepts one documents run writes, at most (one call each when they come from topics).</summary>
    public const int ConceptCountMax = 30;
}

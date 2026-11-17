namespace TMConcept.Core.Documents;

/// <summary>Sizes and limits of "Belgelerim" (documents → concepts, 0.1.21).</summary>
public static class DocumentLimits
{
    /// <summary>Largest file that can be added (bigger ones are refused before anything is copied).</summary>
    public const long FileBytesMax = 50L * 1024 * 1024;

    /// <summary>Largest web page or article body read from the network.</summary>
    public const long WebBytesMax = 5L * 1024 * 1024;

    /// <summary>Longest wait for one web page or article (seconds).</summary>
    public const int WebSecondsMax = 30;

    /// <summary>Most characters of text kept from one document (about 1,500 pages); the rest is left out and the document says so.</summary>
    public const int CharactersMax = 3_000_000;

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

namespace TMConcept.Core.Research;

/// <summary>
/// Limits of "Araştırmadan" (0.1.22). A run searches, shows what it would fetch, and fetches only the ticked pages within a page count, a
/// byte budget and a time limit; it can be canceled at any time. What a call sends to the model is fitted by the documents' passage budget
/// (a local 8k model gets a few passages, an online one more), so these limits only bound the network and the disk.
/// </summary>
public static class ResearchLimits
{
    /// <summary>Pages fetched by default.</summary>
    public const int PagesDefault = 6;

    /// <summary>Pages one run fetches at most.</summary>
    public const int PagesMax = 12;

    /// <summary>Hits asked of one backend for one query.</summary>
    public const int HitsPerQuery = 5;

    /// <summary>Candidates shown after a search at most.</summary>
    public const int CandidatesMax = 30;

    /// <summary>Languages one run reads at most (the profile's, English and the ones the subject or the user adds).</summary>
    public const int LanguagesMax = 4;

    /// <summary>Languages that are searched directly (the profile's and English); the others are reached through the found articles' language links.</summary>
    public const int SearchLanguagesMax = 2;

    /// <summary>Bytes all fetched pages of one run may hold together.</summary>
    public const long BytesMax = 4L * 1024 * 1024;

    /// <summary>Longest the search, and again the fetching, of one run may take (seconds).</summary>
    public const int SecondsMax = 120;

    /// <summary>Requests that run at the same time (kind to the free services).</summary>
    public const int Concurrency = 2;

    /// <summary>Addresses the user may give at most.</summary>
    public const int UrlsMax = 10;

    /// <summary>Longest subject (characters).</summary>
    public const int SubjectMax = 300;

    /// <summary>Facts read from one Wikidata item at most.</summary>
    public const int FactsMax = 300;

    /// <summary>Characters of one MCP tool answer kept at most.</summary>
    public const int ToolTextMax = 400_000;

    /// <summary>Longest MCP message read (bytes), so a broken server cannot fill the memory.</summary>
    public const int McpMessageBytesMax = 8 * 1024 * 1024;

    /// <summary>Longest wait for one MCP answer (seconds).</summary>
    public const int McpSecondsMax = 60;

    /// <summary>MCP servers the user may add at most.</summary>
    public const int McpServersMax = 8;
}

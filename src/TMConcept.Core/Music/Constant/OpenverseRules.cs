namespace TMConcept.Core.Music;

/// <summary>
/// The Openverse audio search (0.1.45, S6): no key, two requests a page (commercial use with changes allowed, and CC0), only CC0, CC BY and CC
/// BY-SA kept (checked again here: a non-commercial or no-derivatives result is never offered).
/// </summary>
public static class OpenverseRules
{
    /// <summary>The audio search address.</summary>
    public const string SearchAddress = "https://api.openverse.org/v1/audio/";

    /// <summary>Results per request (the most an anonymous request gets).</summary>
    public const int PageSize = 20;

    /// <summary>The longest search text sent.</summary>
    public const int MaxQuery = 200;

    /// <summary>The largest track downloaded (bytes).</summary>
    public const long MaxTrackBytes = 60L << 20;

    /// <summary>The longest wait for a search or a download.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>The licence codes kept: CC0, CC BY, CC BY-SA (no NC, no ND, no public domain mark).</summary>
    public static readonly IReadOnlyList<string> Allowed = ["cc0", "by", "by-sa"];

    /// <summary>The file kinds a picked track may have.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".mp3", ".ogg", ".oga", ".opus", ".wav", ".flac", ".m4a", ".aac"];

    /// <summary>The folder of picked tracks under the data folder.</summary>
    public const string Folder = "music";
}

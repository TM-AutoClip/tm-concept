using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The source MAGNeT runs from (28 Nov 2026): Meta's audiocraft (MIT, github.com/facebookresearch/audiocraft, version 1.4.0a2) at the newest
/// commit of its main branch (13 Mar 2025), as GitHub's source archive pinned by its SHA-256 (measured on 28 Nov 2026). It is unpacked and put
/// first on the script's module path, never installed as a package (its <c>setup.py</c> would pull av 11 and pesq, which have no Windows
/// builds). Downloaded only after the user's yes (rule 18).
/// </summary>
public static class AudiocraftPins
{
    /// <summary>The commit.</summary>
    public const string Commit = "896ec7c47f5e5d1e5aa1e4b260c4405328bf009d";

    /// <summary>The archive's size (bytes).</summary>
    public const long ArchiveBytes = 8_213_714;

    /// <summary>The source archive (its check file is MAGNeT's model code).</summary>
    public static readonly ToolArchive Archive = new(ToolKind.MusicCode, Commit[..12], "any",
        new Uri("https://codeload.github.com/facebookresearch/audiocraft/zip/" + Commit),
        "e43f33032cbe253c8d09f561b38a5fb0482b92e9246c457ec65453c30cfce030", "audiocraft-" + Commit, "audiocraft/models/magnet.py", ArchiveBytes);
}

using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The source SongGeneration runs from (28 Nov 2026): Tencent AI Lab's code at commit b1b03ec (the newest commit of its main branch on 30 Mar
/// 2026, the Wayback Machine's copy of its GitHub page). The official repository github.com/tencent-ailab/SongGeneration was deleted after
/// that, so the archive comes from a fork that still holds exactly this commit, pinned by its SHA-256 (measured on 28 Nov 2026; a second fork's
/// archive differs only in its root folder's name). If the fork disappears too, the install fails with the download's error; nothing else is
/// tried. Its code is under Tencent's "License Terms of SongGeneration" (non-commercial). The script imports it from the folder; its bash
/// starter and its requirements files are not used. Downloaded only after the user's yes (rule 18).
/// </summary>
public static class SongGenerationPins
{
    /// <summary>The commit.</summary>
    public const string Commit = "b1b03ec93a38ae4a566ad8a22010f51dd49cb803";

    /// <summary>The source archive (5,102,947 bytes; its check file is the generation code).</summary>
    public static readonly ToolArchive Archive = new(ToolKind.MusicCode, Commit[..12], "any",
        new Uri("https://codeload.github.com/youngsuen19860205/tencent-ailab-songgeneration/zip/" + Commit),
        "8aaed3824f5eb182aa90750c4a08672ecea63b8a0b837f9afad6a7c366fe8138", "tencent-ailab-songgeneration-" + Commit, "generate.py", 5_102_947);
}

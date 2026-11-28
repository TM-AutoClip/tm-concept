using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The sources YuE runs from (28 Nov 2026), GitHub source archives pinned by their SHA-256 (measured on 28 Nov 2026), never installed as
/// packages (the scripts import them from their folders). Downloaded only after the user's yes (rule 18).
/// </summary>
public static class YuePins
{
    /// <summary>YuE2's commit: multimodal-art-projection/YuE main of 1 Nov 2026, with the low-memory mode and the Windows attention fix.</summary>
    public const string Yue2Commit = "1116bea76032bee5668e5f097294d082fa0b84f0";

    /// <summary>YuE-exllamav2's commit (main, 11 Mar 2025).</summary>
    public const string Exl2Commit = "a644036251c96613e0c4bb192a9309bfc046dafd";

    /// <summary>
    /// YuE2's code (Apache-2.0; 5,772,049 bytes). Its release wheel (0.1.6) predates the low-memory mode, so the source is used; its
    /// <c>pyproject.toml</c> pins PyTorch 2.10.0, which PyPI has only for the processor on Windows, so the app's own lock installs the CUDA build.
    /// </summary>
    public static readonly ToolArchive Yue2 = new(ToolKind.MusicCode, Yue2Commit[..12], "any",
        new Uri("https://codeload.github.com/multimodal-art-projection/YuE/zip/" + Yue2Commit),
        "0f56a66d74a0cd1372fd542f97d273aa5f4a09465ecc0937cbcde4841fd8d7d4", "YuE-" + Yue2Commit, "src/yue2/pipeline.py", 5_772_049);

    /// <summary>
    /// YuE-exllamav2 (Apache-2.0; sgsdxzy's version of YuE 1, which the official README points to for small cards; 5,114,201 bytes): it runs
    /// the original 16-bit models too, so all three YuE 1 models run from it. It carries its own copy of the codec code; only the codec's
    /// weights come from Hugging Face.
    /// </summary>
    public static readonly ToolArchive Exl2 = new(ToolKind.MusicCode, Exl2Commit[..12], "any",
        new Uri("https://codeload.github.com/sgsdxzy/YuE-exllamav2/zip/" + Exl2Commit),
        "8f82c88a2411cda3797c28b239a837e57a3db840727b930efc3056c98f06f0e9", "YuE-exllamav2-" + Exl2Commit, "src/yue/infer_stage1.py", 5_114_201);
}

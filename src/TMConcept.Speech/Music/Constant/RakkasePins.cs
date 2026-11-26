using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The pinned downloads of the local music model (0.1.46, C5). rakkase (MIT, github.com/alexturkey/rakkase) drives ACE-Step 1.5 (MIT,
/// github.com/ACE-Step/ACE-Step-1.5) for a 4 GB NVIDIA card; its <c>scripts/kur.ps1</c> (read on 24 Nov 2026) clones ACE-Step at one commit,
/// runs <c>uv python install 3.11</c> and <c>uv sync --frozen</c> in it, downloads the weights listed in <c>scripts/modeller.json</c> into
/// <c>&lt;ACE&gt;/checkpoints</c> (each file pinned by its SHA-256 at one Hugging Face revision) and writes <c>ayarlar.json</c>. The app does the
/// same steps itself with its own uv and Python (no git, no winget, no system Python): both source trees come as GitHub source archives at
/// those commits, pinned by the SHA-256 of the archive (measured on 24 Nov 2026). Nothing here is downloaded without the user's yes (rule 18).
/// </summary>
public static class RakkasePins
{
    /// <summary>rakkase's commit (main, 23 Nov 2026).</summary>
    public const string RakkaseCommit = "51b1375e794abd1bbc36e5f4cda76df3645acd08";

    /// <summary>ACE-Step 1.5's commit (the one rakkase's install pins and measured).</summary>
    public const string AceCommit = "ca1e85fe9430179831e6bc6be790c332190a3866";

    /// <summary>The Hugging Face revision of the weights (rakkase's <c>scripts/modeller.json</c>).</summary>
    public const string WeightsRevision = "19671f406d603126926c1b7e2adc169acbcade22";

    /// <summary>The host of the source archives.</summary>
    public const string ArchiveHost = "codeload.github.com";

    /// <summary>The host of the weights (its file servers under <c>.hf.co</c> are allowed redirect targets).</summary>
    public const string WeightsHost = "huggingface.co";

    /// <summary>The size of rakkase's source archive (bytes).</summary>
    public const long RakkaseArchiveBytes = 6_646_725;

    /// <summary>The size of ACE-Step 1.5's source archive (bytes).</summary>
    public const long AceArchiveBytes = 9_148_700;

    /// <summary>rakkase's source archive.</summary>
    public static readonly ToolArchive Rakkase = new(ToolKind.MusicCode, RakkaseCommit[..12], "any",
        new Uri("https://codeload.github.com/alexturkey/rakkase/zip/" + RakkaseCommit),
        "e2a8d36afb9b67d675d90b6752b3c43d98a94a0f6bac9cd56d4098f2f1fbb2cd", "rakkase-" + RakkaseCommit, "rakkase.py", RakkaseArchiveBytes);

    /// <summary>ACE-Step 1.5's source archive (its <c>pyproject.toml</c> and <c>uv.lock</c> pin every package, PyTorch for CUDA 12.8 included).</summary>
    public static readonly ToolArchive Ace = new(ToolKind.MusicCode, AceCommit[..12], "any",
        new Uri("https://codeload.github.com/ACE-Step/ACE-Step-1.5/zip/" + AceCommit),
        "108e45dffc7eb62af98d781c4159aa63a2b7a241186734e3e065279da7fc198d", "ACE-Step-1.5-" + AceCommit, "pyproject.toml", AceArchiveBytes);

    /// <summary>
    /// About what ACE-Step's packages take (PyTorch 2.7.1 for CUDA 12.8 and the rest, with uv's cache): rakkase's whole install less the weights
    /// and the two source archives.
    /// </summary>
    public const long PackageBytes = Core.Music.MusicGenerationRules.LocalInstallBytes - Core.Music.MusicGenerationRules.LocalWeightBytes - RakkaseArchiveBytes - AceArchiveBytes;

    /// <summary>The name of a run in rakkase's plan (its output folder).</summary>
    public const string RunName = "tmc";
}

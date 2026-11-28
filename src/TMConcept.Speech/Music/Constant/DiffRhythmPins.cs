using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// The sources DiffRhythm runs from (28 Nov 2026): ASLP-lab's DiffRhythm (Apache-2.0, github.com/ASLP-lab/DiffRhythm) as GitHub's source
/// archives pinned by their SHA-256 (measured on 28 Nov 2026). Two commits, because the newest one hard-codes the 1.2 models and changed the
/// sampler's signature (the 1.0 models would load without their duration embedding and no error): <see cref="Current"/> for DiffRhythm 1.2,
/// <see cref="Legacy"/> (the last commit before "v1.2 update") for DiffRhythm 1.0. Most of each archive is its Chinese lexicon and example
/// audio. The script imports its model code and helpers from the folder; nothing is installed as a package. Downloaded only after the user's
/// yes (rule 18).
/// </summary>
public static class DiffRhythmPins
{
    /// <summary>The commit of DiffRhythm 1.2's code (main, 27 Nov 2025).</summary>
    public const string CurrentCommit = "28ad63c0f096fe2ee258bcabbcf081d5d9366afd";

    /// <summary>The commit of DiffRhythm 1.0's code (20 Apr 2025).</summary>
    public const string LegacyCommit = "4b5ac626337b8bd0d3929316af52fa8088112ebe";

    /// <summary>The code version of <see cref="Current"/>, as the script is told (<c>--code-version</c>).</summary>
    public const string CurrentVersion = "1.2";

    /// <summary>The code version of <see cref="Legacy"/>, as the script is told.</summary>
    public const string LegacyVersion = "1.0";

    /// <summary>DiffRhythm 1.2's source archive (115,180,822 bytes; its check file is the inference helpers).</summary>
    public static readonly ToolArchive Current = new(ToolKind.MusicCode, CurrentCommit[..12], "any",
        new Uri("https://codeload.github.com/ASLP-lab/DiffRhythm/zip/" + CurrentCommit),
        "39378aac254976fdb26ec6313e54312cabb8c0f3cec7d0a7c3cfc0db9f6c2aba", "DiffRhythm-" + CurrentCommit, "infer/infer_utils.py", 115_180_822);

    /// <summary>DiffRhythm 1.0's source archive (130,373,239 bytes).</summary>
    public static readonly ToolArchive Legacy = new(ToolKind.MusicCode, LegacyCommit[..12], "any",
        new Uri("https://codeload.github.com/ASLP-lab/DiffRhythm/zip/" + LegacyCommit),
        "58e99170ad096364951efdf332aad7fd7ef2e8a185121f882f0932e2c4e5ecfd", "DiffRhythm-" + LegacyCommit, "infer/infer_utils.py", 130_373_239);
}

namespace TMConcept.Speech.Music;

/// <summary>
/// What rakkase's plan check asks of every part (commit 51b1375, <c>araclar/denetle.py</c> and <c>docs/KULLANIM.md</c>, read on 26 Nov 2026):
/// <c>ad</c>, <c>caption</c>, <c>sure</c>, <c>tohum</c>, <c>bpm</c>, <c>ton</c> and <c>olcu</c> are required. <c>bpm</c> is a number from 30 to
/// 300, <c>olcu</c> the time signature as a STRING ("2", "3", "4" or "6"; the check and the package call <c>.strip()</c> on it), <c>ton</c> a
/// string where an empty one lets the model choose the key (ACE-Step's own default <c>keyscale=""</c>). A part without one of them stops the run
/// before anything is made (<c>RK-E-PLAN</c>, exit code 2): 0.1.54 left out <c>ton</c> and <c>olcu</c>, and <c>bpm</c> without a reference.
/// </summary>
public static class RakkasePlanRules
{
    /// <summary>The slowest tempo the plan check takes.</summary>
    public const int MinBpm = 30;

    /// <summary>The fastest tempo the plan check takes.</summary>
    public const int MaxBpm = 300;

    /// <summary>The longest part the plan check takes (seconds; above 300 s it only warns).</summary>
    public const int MaxSeconds = 600;

    /// <summary>
    /// The most characters of a part's <c>caption</c> (0.1.55). The plan check also stops a part whose caption with ACE-Step's template and
    /// meta lines is longer than 256 tokens of its text encoder (Qwen3-Embedding-0.6B's tokenizer; <c>HATA: caption+metas N token &gt; 256</c>).
    /// Counted with that tokenizer on 26 Nov 2026: 600 characters of Turkish are 267 tokens with the rest, 500 are 228, 600 of English 184;
    /// 400 characters stay below the limit in both with room for denser words. A longer prompt is cut at a word (<see cref="RakkaseMusicGenerator.Caption"/>).
    /// </summary>
    public const int MaxCaptionCharacters = 400;

    /// <summary>The share of <see cref="MaxCaptionCharacters"/> below which a cut does not go back to a space (one very long word).</summary>
    public const double WordCutShare = 0.5;

    /// <summary>The key: empty = the model chooses it.</summary>
    public const string AnyKey = "";

    /// <summary>The time signature (4/4), written as a string.</summary>
    public const string Meter = "4";

    /// <summary>The start of a line of the plan check that stopped the run (<c>"  HATA: zorunlu alan yok: bpm"</c>).</summary>
    public const string ErrorMark = "HATA:";

    /// <summary>The plan check's summary line (<c>"DENETIM SONUC: 1 parca, 3 hata, 0 uyari"</c>).</summary>
    public const string ResultMark = "DENETIM SONUC";

    /// <summary>The start of rakkase's own error codes (<c>[RK-E-PLAN]</c>).</summary>
    public const string CodeMark = "RK-E-";

    /// <summary>The most lines of a failed run written to the log.</summary>
    public const int LogLines = 40;

    /// <summary>The most characters of a failed run written to the log.</summary>
    public const int LogCharacters = 4000;

    /// <summary>The most characters of the reason a failure shows on screen.</summary>
    public const int ReasonCharacters = 600;
}

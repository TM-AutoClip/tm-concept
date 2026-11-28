namespace TMConcept.Speech.Music;

/// <summary>
/// How the app and the local music families' scripts talk (28 Nov 2026): a script prints its stages as <c>TMC: …</c> lines (written to the
/// log as they come), its progress as <c>TMC-P: …</c> lines (0.1.61) and a failure as one <c>TMC-E-&lt;CODE&gt;: …</c> line before it exits with 2 (that line is the reason on screen; the
/// whole tail goes to the log, as for rakkase since 0.1.55). The numbers the scripts are given come from here.
/// </summary>
public static class LocalMusicRunRules
{
    /// <summary>The start of a stage line.</summary>
    public const string StageMark = "TMC:";

    /// <summary>
    /// The start of a progress line (0.1.61): <c>TMC-P: &lt;stage&gt;/&lt;stages&gt; &lt;done&gt;/&lt;total&gt; &lt;unit&gt;</c>, printed by the
    /// scripts' <c>tmc_progress.py</c> at most once per whole per cent and once a second (<see cref="MusicProgressLineParser"/> reads it).
    /// </summary>
    public const string ProgressMark = "TMC-P:";

    /// <summary>The start of a failure line.</summary>
    public const string ErrorMark = "TMC-E-";

    /// <summary>The failure line of a graphics card that ran out of memory (the run's failure becomes <c>MusicFailure.Memory</c>).</summary>
    public const string MemoryMark = "TMC-E-MEMORY";

    /// <summary>The prompt's file in the piece's folder (UTF-8; a prompt is never put on the command line).</summary>
    public const string PromptFile = "prompt.txt";

    /// <summary>The words' file in the piece's folder (UTF-8; a family that sings; empty for an instrumental piece).</summary>
    public const string LyricsFile = "lyrics.txt";

    /// <summary>The piece's file in the piece's folder.</summary>
    public const string PieceFile = "piece.wav";

    /// <summary>MusicGen's classifier-free guidance (its <c>generation_config.json</c>: <c>guidance_scale</c> 3.0).</summary>
    public const string MusicGenGuidance = "3.0";

    /// <summary>
    /// AudioLDM 2's denoising steps with the DPM-Solver++ scheduler the script switches to (the Hugging Face blog's speed-up; the pipeline's
    /// own 200 DDIM steps are far slower on a small card).
    /// </summary>
    public const string AudioLdm2Steps = "50";

    /// <summary>AudioLDM 2's guidance (the pipeline's default, 3.5).</summary>
    public const string AudioLdm2Guidance = "3.5";

    /// <summary>The most lines of a failed run written to the log.</summary>
    public const int LogLines = 40;

    /// <summary>The most characters of a failed run written to the log.</summary>
    public const int LogCharacters = 4000;

    /// <summary>The most characters of the reason a failure shows on screen.</summary>
    public const int ReasonCharacters = 600;
}

namespace TMConcept.Core.Music;

/// <summary>
/// Reads a local music script's progress line (0.1.61): "TMC-P: 2/3 16/40 blocks" → stage 2 of 3, 16 of 40 blocks. Every local music family
/// prints the same line (its scripts share <c>tmc_progress.py</c>); the generators hand their output lines to this one reader.
/// </summary>
public interface IMusicProgressParser
{
    /// <summary>The progress <paramref name="line"/> reports; null when it is not a progress line (or a malformed one).</summary>
    MusicProgress? Parse(string line);
}

namespace TMConcept.Core.Music;

/// <summary>What a stage of a local music run counts (0.1.61; <see cref="MusicProgress"/>). Never written to disk.</summary>
public enum MusicProgressUnit
{
    /// <summary>Nothing (a piece of an online model, ACE-Step: an indeterminate bar with the time so far).</summary>
    None,

    /// <summary>Generated tokens (MusicGen, SongGeneration, YuE2's score and song, YuE 1's stage 1).</summary>
    Tokens,

    /// <summary>YuE 1's 6-second blocks (its stage 2 refines the tokens block by block).</summary>
    Blocks,

    /// <summary>Denoising or decoding steps (MAGNeT, AudioLDM 2, DiffRhythm, YuE2's synthesis).</summary>
    Steps,

    /// <summary>The decoding into sound (counted only where the model reports it: YuE2; otherwise a stage without a count).</summary>
    Decoding,
}

namespace TMConcept.Core.Music;

/// <summary>
/// Writes the score of a piece (0.1.55, "Program composes"): the notes of every instrument, which a SoundFont synthesizer then plays
/// (<see cref="IMidiRenderer"/>). Today the app composes it itself from the prompt's words (<see cref="ProgramScoreWriter"/>, no model); a writer
/// that asks a text model for the score would be another implementation of this contract.
/// </summary>
public interface IScoreWriter
{
    /// <summary>The score of <paramref name="brief"/> (the same brief with the same seed gives the same score).</summary>
    MusicScore Write(ScoreBrief brief);
}

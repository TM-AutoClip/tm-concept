namespace TMConcept.Speech.AhenkCep;

/// <summary>
/// One piece of a scene as the model reads it: its text (lower case, only the model's characters), the pause after it in seconds, the id
/// of every character, the word each character belongs to and the index where that word starts.
/// </summary>
internal sealed record AhenkCepPiece(string Text, double Pause, long[] Ids, long[] Words, long[] WordStarts);

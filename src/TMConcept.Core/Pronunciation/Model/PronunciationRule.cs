namespace TMConcept.Core.Pronunciation;

/// <summary>
/// One word of a language's dictionary as it is applied: the built-in ones and the user's (<see cref="Entry"/>: the user's own word).
/// <see cref="ExactCase"/>: matched only as written, letter case included ("IT" but not "it"; written "!IT" in a list or by the user).
/// <see cref="Category"/>: the "##" heading it stands under in the built-in list (empty for the user's words).
/// </summary>
public sealed record PronunciationRule(string Word, string Spoken, PronunciationEntry? Entry, bool ExactCase = false, string Category = "")
{
    /// <summary>The mark in front of a word that is matched only as written.</summary>
    public const char ExactMark = '!';

    /// <summary>Whether it comes with the app (the user's word of the same spelling replaces it).</summary>
    public bool BuiltIn => Entry is null;
}

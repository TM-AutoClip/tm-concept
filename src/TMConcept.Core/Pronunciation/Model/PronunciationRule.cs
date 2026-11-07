namespace TMConcept.Core.Pronunciation;

/// <summary>One word of a language's dictionary as it is applied: the built-in ones and the user's (<see cref="Entry"/>: the user's own word).</summary>
public sealed record PronunciationRule(string Word, string Spoken, PronunciationEntry? Entry)
{
    /// <summary>Whether it comes with the app (the user's word of the same spelling replaces it).</summary>
    public bool BuiltIn => Entry is null;
}

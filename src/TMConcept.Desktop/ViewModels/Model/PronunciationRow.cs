using TMConcept.Core.Pronunciation;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A word of the pronunciation list: built in (no delete; editing it adds the user's own word of the same spelling) or the user's own.
/// <see cref="Shown"/> writes an exact-case word as "!WORD", as the user types one.
/// </summary>
public sealed record PronunciationRow(string Word, string Spoken, PronunciationEntry? Entry, string Tag, bool ExactCase = false, string Category = "")
{
    /// <summary>Whether the user added it (only those can be removed).</summary>
    public bool Own => Entry is not null;

    /// <summary>The word as the list shows it ("!IT" for a word matched only as written).</summary>
    public string Shown => ExactCase ? PronunciationRule.ExactMark + Word : Word;
}

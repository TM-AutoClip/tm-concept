using TMConcept.Core.Pronunciation;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A word of the pronunciation list: built in (no delete) or the user's own.</summary>
public sealed record PronunciationRow(string Word, string Spoken, PronunciationEntry? Entry, string Tag)
{
    /// <summary>Whether the user added it (only those can be removed).</summary>
    public bool Own => Entry is not null;
}

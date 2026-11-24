namespace TMConcept.Desktop.ViewModels;

/// <summary>One note on a made video (F16, 0.1.49): the note as kept and its line ("0:42 · Scene 3").</summary>
public sealed class TimeNoteRowViewModel(Core.Videos.TimeNote note, string line)
{
    /// <summary>The note.</summary>
    public Core.Videos.TimeNote Note { get; } = note;

    /// <summary>"0:42 · Scene 3".</summary>
    public string Line { get; } = line;

    /// <summary>The text.</summary>
    public string Text { get; } = note.Text;
}

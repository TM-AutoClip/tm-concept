namespace TMConcept.Desktop.Dialogs;

/// <summary>One version in the update window: "0.1.19 · 12 November 2026" and its notes in the app's language as readable text.</summary>
public sealed record ReleaseNoteRow(string Header, string Notes);

using CommunityToolkit.Mvvm.Input;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Updates;

namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// The "What's new" window (0.1.23): the notes of every version it was given, newest first, in the app's language (the Turkish part for a
/// Turkish interface; English otherwise), with the Markdown read as plain text like the update window (<see cref="ReleaseNotesText"/>).
/// </summary>
public sealed partial class WhatsNewViewModel(IReadOnlyList<VersionNotes> versions, Localizer text)
{
    private IReadOnlyList<ReleaseNoteRow>? _rows;

    /// <summary>Raised when the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>"What's new".</summary>
    public string Title => text["whatsnew.title"];

    /// <summary>"Version 0.1.23" for one version; "Changes from 0.1.18 to 0.1.23 (6 versions)" for several.</summary>
    public string Subtitle => Versions.Count switch
    {
        0 => text["whatsnew.none"],
        1 => text.Format("whatsnew.subtitle.one", versions[0].Version),
        _ => text.Format("whatsnew.subtitle.many", versions.Min(v => v.Version), versions.Max(v => v.Version), versions.Count),
    };

    /// <summary>One row per version: "0.1.23" and its notes as readable text.</summary>
    public IReadOnlyList<ReleaseNoteRow> Versions => _rows ??= versions
        .OrderByDescending(v => v.Version)
        .Select(v => new ReleaseNoteRow(v.Version.ToString(), ReleaseNotesText.For(v.Body, text.Language) is { Length: > 0 } notes ? notes : text["appupdate.no_notes"]))
        .ToList();

    /// <summary>Closes the window.</summary>
    [RelayCommand]
    public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}

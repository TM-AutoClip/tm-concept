using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Documents;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One document of "Belgelerim" with its tick box: ticked documents are the ones "Belgelerden" uses (none ticked = every ready one).
/// <see cref="Info"/> is its line ("PDF · 12 pages · 34 passages · 1.2 MB"); <see cref="Problem"/> says why it cannot be used, when it cannot.
/// 0.1.25: a PDF's kept pages (<see cref="PagesLine"/>) and, when some are still to read, the button that reads only those.
/// </summary>
public sealed partial class DocumentRowViewModel(SourceDocument document, string info, string problem) : ObservableObject
{
    /// <summary>The document.</summary>
    public SourceDocument Document { get; } = document;

    /// <summary>Its id.</summary>
    public Guid Id => Document.Id;

    /// <summary>Its title.</summary>
    public string Title => Document.Title;

    /// <summary>Its line.</summary>
    public string Info { get; } = info;

    /// <summary>Why it cannot be used (empty when it can).</summary>
    public string Problem { get; } = problem;

    /// <summary>Whether it cannot be used.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>Whether it can be searched.</summary>
    public bool Ready => Document.Status == DocumentStatus.Ready && Document.Chunks > 0;

    /// <summary>Ticked: "Belgelerden" uses it.</summary>
    [ObservableProperty]
    public partial bool Checked { get; set; }

    /// <summary>Which pages are read and kept, and which are still to read (0.1.25; unknown for a document added before or not a PDF).</summary>
    public DocumentPageState Pages { get; init; } = DocumentPageState.Unknown;

    /// <summary>"Pages read: 680 of 700 · 20 still to read" (empty when unknown).</summary>
    public string PagesLine { get; init; } = string.Empty;

    /// <summary>Whether the pages line is shown.</summary>
    public bool HasPagesLine => PagesLine.Length > 0;

    /// <summary>Pages still to read.</summary>
    public int MissingCount => Pages.Missing.Count;

    /// <summary>"Read the 20 missing pages".</summary>
    public string ReadMissingText { get; init; } = string.Empty;

    /// <summary>Whether its missing pages can be read now (an engine is chosen for "Text from scanned pages").</summary>
    [ObservableProperty]
    public partial bool CanReadMissing { get; set; }

    /// <summary>Whether it has pages still to read.</summary>
    public bool HasMissing => MissingCount > 0;

    /// <summary>What reading the missing pages takes with the chosen engine (an estimate), or how to choose one.</summary>
    [ObservableProperty]
    public partial string ReadMissingEstimate { get; set; } = string.Empty;
}

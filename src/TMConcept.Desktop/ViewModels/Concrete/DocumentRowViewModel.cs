using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Documents;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One document of "Belgelerim" with its tick box: ticked documents are the ones "Belgelerden" uses (none ticked = every ready one).
/// <see cref="Info"/> is its line ("PDF · 12 pages · 34 passages · 1.2 MB"); <see cref="Problem"/> says why it cannot be used, when it cannot.
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
}

using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Research;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A page the research found, shown BEFORE anything is fetched: its title, where it comes from ("Wikipedia · el"), its address and short
/// description as the tooltip, and its tick box (only ticked pages are fetched).
/// </summary>
public sealed partial class ResearchCandidateViewModel(ResearchHit hit, string source, bool picked) : ObservableObject
{
    /// <summary>The hit (fetched by its own backend).</summary>
    public ResearchHit Hit { get; } = hit;

    /// <summary>The title.</summary>
    public string Title => Hit.Title;

    /// <summary>Where it comes from and its language ("Wikipedia · el").</summary>
    public string Source { get; } = source;

    /// <summary>The address and the short description.</summary>
    public string Detail { get; } = string.Join("\n", new[] { hit.Url, hit.Snippet }.Where(t => t.Length > 0));

    /// <summary>Ticked: fetched when the section writes.</summary>
    [ObservableProperty]
    public partial bool Checked { get; set; } = picked;
}

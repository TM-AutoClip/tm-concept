using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One suggested topic on the concept page with its tick box. The tick list lives on the page only (cleared on a profile switch or a
/// restart); <see cref="Id"/> is the kept suggestion's id, which only remembers that it was suggested.
/// </summary>
public sealed partial class TopicItemViewModel(Guid id, string text) : ObservableObject
{
    /// <summary>The kept suggestion's id.</summary>
    public Guid Id { get; } = id;

    /// <summary>The topic (one line).</summary>
    public string Text { get; } = text;

    /// <summary>Ticked: "Generate selected" writes one concept for it.</summary>
    [ObservableProperty]
    public partial bool Checked { get; set; }
}

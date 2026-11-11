using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One topic on the concept page: a suggestion with its tick box (the offered list) or a row of the selected list (one concept each).
/// <see cref="Id"/> is the kept suggestion's id, so its status (offered, selected, made) can be changed in the store.
/// </summary>
public sealed partial class TopicItemViewModel(Guid id, string text, DateTimeOffset suggestedAtUtc) : ObservableObject
{
    /// <summary>The kept suggestion's id.</summary>
    public Guid Id { get; } = id;

    /// <summary>The topic (one line).</summary>
    public string Text { get; } = text;

    /// <summary>When it was suggested (the selected list is kept in this order, like the store).</summary>
    public DateTimeOffset SuggestedAtUtc { get; } = suggestedAtUtc;

    /// <summary>Ticked in the offered list (only there; the selected list has no tick boxes).</summary>
    [ObservableProperty]
    public partial bool Checked { get; set; }
}

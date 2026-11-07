using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Concepts;

namespace TMConcept.Desktop.ViewModels;

/// <summary>One focus drop-down of the concept page ("Period", "Tone"): its choices in the interface language, the first one "—" (none).</summary>
public sealed partial class FocusRowViewModel : ObservableObject
{
    /// <summary>Creates the row of <paramref name="dimension"/>.</summary>
    public FocusRowViewModel(FocusDimension dimension, string title, IReadOnlyList<ChoiceOption> options)
    {
        Dimension = dimension;
        Title = title;
        Options = options;
        Selected = options.FirstOrDefault();
    }

    /// <summary>The dimension.</summary>
    public FocusDimension Dimension { get; }

    /// <summary>Its name.</summary>
    public string Title { get; }

    /// <summary>"—" first, then the choices (value = option id).</summary>
    public IReadOnlyList<ChoiceOption> Options { get; }

    /// <summary>The chosen one ("—" = none).</summary>
    [ObservableProperty]
    public partial ChoiceOption? Selected { get; set; }

    /// <summary>The chosen option (null for "—").</summary>
    public FocusOption? Chosen => Dimension.Options.FirstOrDefault(o => o.Id == Selected?.Value);
}

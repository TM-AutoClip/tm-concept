using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Pipeline;

namespace TMConcept.Desktop.ViewModels;

/// <summary>One step of the step model table: its models (empty choice first), the recommended ones, effort.</summary>
public sealed partial class StepModelRowViewModel(PipelineStep step, string title, string recommended) : ObservableObject
{
    /// <summary>Step.</summary>
    public PipelineStep Step { get; } = step;

    /// <summary>Title.</summary>
    public string Title { get; } = title;

    /// <summary>"Recommended: …" text.</summary>
    public string Recommended { get; } = recommended;

    /// <summary>Models of the step's capability.</summary>
    public ObservableCollection<ModelOption> Options { get; } = [];

    /// <summary>Chosen model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Efforts), nameof(HasEfforts))]
    public partial ModelOption? Selected { get; set; }

    /// <summary>Effort levels of the chosen model (empty first = the model's default).</summary>
    public IReadOnlyList<string> Efforts => Selected?.Model is { Efforts.Count: > 0 } m ? ["", .. m.Efforts] : [];

    /// <summary>Whether effort can be chosen.</summary>
    public bool HasEfforts => Efforts.Count > 0;

    /// <summary>Chosen effort.</summary>
    [ObservableProperty]
    public partial string? Effort { get; set; }
}

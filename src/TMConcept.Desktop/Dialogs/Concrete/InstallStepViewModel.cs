using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Components;

namespace TMConcept.Desktop.Dialogs;

/// <summary>One line of the install window: a component, its state, its own progress and what it is doing (or why it failed).</summary>
public sealed partial class InstallStepViewModel(string code, string name) : ObservableObject
{
    /// <summary>Component code.</summary>
    public string Code { get; } = code;

    /// <summary>Display name.</summary>
    public string Name { get; } = name;

    /// <summary>State.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(Running), nameof(Failed))]
    public partial ComponentStepState State { get; set; }

    /// <summary>Own progress 0–100.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>What is downloaded or unpacked now, the reason of a failure, or the state in words.</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>Whether it is being installed now.</summary>
    public bool Running => State == ComponentStepState.Running;

    /// <summary>Whether it could not be installed.</summary>
    public bool Failed => State is ComponentStepState.Failed or ComponentStepState.Skipped;

    /// <summary>Glyph by state.</summary>
    public string Glyph => State switch
    {
        ComponentStepState.Running => "⟳",
        ComponentStepState.Done => "✔",
        ComponentStepState.Failed => "✖",
        ComponentStepState.Skipped => "–",
        _ => "•",
    };
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A tool row: name, what it is for, state, install button and progress.</summary>
public sealed partial class ToolRowViewModel(string code, string name, string purpose) : ObservableObject
{
    /// <summary>Code ("node", "ffmpeg", "engine:hyperframes" …).</summary>
    public string Code { get; } = code;

    /// <summary>Name.</summary>
    public string Name { get; } = name;

    /// <summary>What it is for.</summary>
    public string Purpose { get; } = purpose;

    /// <summary>State text.</summary>
    [ObservableProperty]
    public partial string State { get; set; } = string.Empty;

    /// <summary>Ready.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    public partial bool Ready { get; set; }

    /// <summary>Installing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    public partial bool Busy { get; set; }

    /// <summary>Progress 0–100.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>Whether the install button is shown.</summary>
    public bool CanInstall => !Ready && !Busy;
}

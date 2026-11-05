using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Engines;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A tool or engine row: name, what it is for, state, install button, sample render and progress.</summary>
public sealed partial class ToolRowViewModel(string code, string name, string purpose, IVideoEngine? engine = null,
    Core.Speech.ISpeechEngine? voice = null) : ObservableObject
{
    /// <summary>The voice model of a voice row.</summary>
    public Core.Speech.ISpeechEngine? Voice { get; } = voice;

    /// <summary>Code ("node", "ffmpeg", or the engine kind).</summary>
    public string Code { get; } = code;

    /// <summary>Name.</summary>
    public string Name { get; } = name;

    /// <summary>What it is for.</summary>
    public string Purpose { get; } = purpose;

    /// <summary>The engine of an engine row.</summary>
    public IVideoEngine? Engine { get; } = engine;

    /// <summary>State text.</summary>
    [ObservableProperty]
    public partial string State { get; set; } = string.Empty;

    /// <summary>Ready.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall), nameof(CanSample))]
    public partial bool Ready { get; set; }

    /// <summary>Working.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall), nameof(CanSample))]
    public partial bool Busy { get; set; }

    /// <summary>Progress 0–100.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>Whether the install button is shown.</summary>
    public bool CanInstall => !Ready && !Busy;

    /// <summary>Whether the sample render button is shown.</summary>
    public bool CanSample => Engine is not null && Ready && !Busy;
}

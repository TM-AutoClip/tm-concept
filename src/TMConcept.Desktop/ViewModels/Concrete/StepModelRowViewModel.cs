using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Pipeline;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One step of a page's model pickers: what it is for, its picker (none / off first, the sample images for the images step, then the
/// models: local first), the effort (only for a model that takes one, never in the topics step; first entry "Automatic" sends no effort), the
/// engine (video code step), voice and speed (narration step), a note (license, cost, graphics card memory) and the install progress.
/// </summary>
public sealed partial class StepModelRowViewModel(PipelineStep step, string title, string purpose, string recommended, string automatic) : ObservableObject
{
    /// <summary>Step.</summary>
    public PipelineStep Step { get; } = step;

    /// <summary>Title.</summary>
    public string Title { get; } = title;

    /// <summary>One line: what the step is used for.</summary>
    public string Purpose { get; } = purpose;

    /// <summary>"Recommended: …" text.</summary>
    public string Recommended { get; } = recommended;

    /// <summary>The entries of the picker.</summary>
    public ObservableCollection<StepOption> Options { get; } = [];

    /// <summary>Chosen entry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEfforts), nameof(ShowEngine), nameof(ShowVoice))]
    public partial StepOption? Selected { get; set; }

    /// <summary>Effort levels of the chosen model: "Automatic" (no effort parameter) first; empty for a model without effort levels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEfforts))]
    public partial IReadOnlyList<ChoiceOption> Efforts { get; set; } = [];

    /// <summary>Whether effort can be chosen.</summary>
    public bool HasEfforts => Efforts.Count > 0;

    /// <summary>Chosen effort (value "" = Automatic).</summary>
    [ObservableProperty]
    public partial ChoiceOption? Effort { get; set; }

    /// <summary>Whether this is the video code step (it has the engine picker).</summary>
    public bool IsEngineStep => Step.Code == PipelineSteps.VideoCode;

    /// <summary>The engines.</summary>
    public ObservableCollection<EngineOption> Engines { get; } = [];

    /// <summary>Chosen engine.</summary>
    [ObservableProperty]
    public partial EngineOption? Engine { get; set; }

    /// <summary>Whether the engine picker is shown.</summary>
    public bool ShowEngine => IsEngineStep;

    /// <summary>Whether this is the narration step (voice, speed, listen).</summary>
    public bool IsVoiceStep => Step.Code == PipelineSteps.Speech;

    /// <summary>Whether voice and speed are shown (a voice model is chosen).</summary>
    public bool ShowVoice => IsVoiceStep && Selected is { Kind: StepOptionKind.Model };

    /// <summary>Voices of the chosen narration model.</summary>
    public ObservableCollection<ChoiceOption> Voices { get; } = [];

    /// <summary>Chosen voice.</summary>
    [ObservableProperty]
    public partial ChoiceOption? Voice { get; set; }

    /// <summary>Speech speed.</summary>
    [ObservableProperty]
    public partial decimal? Speed { get; set; } = 1.0m;

    /// <summary>Note under the picker (license, cost, memory).</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Whether the note is a warning (yellow).</summary>
    [ObservableProperty]
    public partial bool NoteIsWarning { get; set; }

    /// <summary>
    /// Why the saved choice was replaced when the page was shown (a voice that does not speak the profile's language, a voice that is gone);
    /// shown first in the note until the user picks something.
    /// </summary>
    public string Fallback { get; set; } = string.Empty;

    /// <summary>The value last loaded or saved (only a changed step is saved and logged).</summary>
    public string Saved { get; set; } = string.Empty;

    partial void OnSelectedChanged(StepOption? value)
    {
        var keep = Effort?.Value ?? string.Empty;
        // The topics step always asks at effort low (a cheap, quick call): no effort picker there.
        Efforts = value?.Model is { Efforts.Count: > 0 } model && Step.Code != PipelineSteps.Topics
            ? [new ChoiceOption(string.Empty, automatic), .. model.Efforts.Select(e => new ChoiceOption(e, e))]
            : [];
        Effort = Efforts.FirstOrDefault(e => e.Value == keep) ?? Efforts.FirstOrDefault();
    }
}

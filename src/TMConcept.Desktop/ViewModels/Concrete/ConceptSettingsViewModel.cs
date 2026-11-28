using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Core.Concepts;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The concept page's bar, shared by everything on the page (0.1.13, the developer: "fine-tuning in one place, applied to both sides; the
/// model choice is common anyway"): the models (concepts, topic suggestions, the meaning check) as one folded summary line, the
/// fine-tuning (tone, audience, telling; "any" until chosen) as one folded line, and the word check. Suggest topics, "Generate selected"
/// and "Generate from an idea" all read their focus, models and checks from here. A profile's last model choices are shown; the
/// fine-tuning starts on "any" for every profile.
/// </summary>
public sealed partial class ConceptSettingsViewModel(IFocusSource focus, Localizer text, StepPickerViewModel picker) : ObservableObject
{
    private bool _loading;
    private bool _pickerFollowed;

    /// <summary>A choice that changes an estimate changed (a model, an effort, the fine-tuning).</summary>
    public event EventHandler? Changed;

    /// <summary>The models of the page as compact cards, three side by side.</summary>
    public StepPickerViewModel Picker { get; } = Layout(picker);

    /// <summary>Whether the models are unfolded (they unfold by themselves while no concept model is chosen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnyOpen))]
    public partial bool ModelsOpen { get; set; }

    /// <summary>The fine-tuning drop-downs (tone, audience, telling), "any" until the user picks one.</summary>
    public ObservableCollection<FocusRowViewModel> FocusRows { get; } = [];

    /// <summary>Whether the fine-tuning is unfolded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnyOpen))]
    public partial bool FineTuneOpen { get; set; }

    /// <summary>
    /// Whether an unfolded panel is shown. It lies over the sections instead of pushing them (one at a time), so the page keeps its layout
    /// at 1024×600 too.
    /// </summary>
    public bool AnyOpen => ModelsOpen || FineTuneOpen;

    /// <summary>Folds the open panel (its Close button, a click beside it).</summary>
    [RelayCommand]
    public void ClosePanel()
    {
        ModelsOpen = false;
        FineTuneOpen = false;
    }

    /// <summary>"Tone: mysterious · Audience: not set · Telling: not set", for the folded line.</summary>
    [ObservableProperty]
    public partial string FineTuneSummary { get; set; } = string.Empty;

    /// <summary>The duplicate check by words (free, on this computer).</summary>
    [ObservableProperty]
    public partial bool LocalCheck { get; set; } = true;

    /// <summary>
    /// Shows the model choices of <paramref name="profile"/> (null = none). The fine-tuning keeps what was chosen (switching tabs does not
    /// reset it); <paramref name="newProfile"/> puts it back on "any", so another profile does not start with a leftover choice.
    /// </summary>
    public async Task LoadAsync(Profile? profile, bool newProfile)
    {
        if (!_pickerFollowed)
        {
            _pickerFollowed = true;
            Picker.Changed += (_, _) =>
            {
                if (!_loading)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            };
        }

        _loading = true;
        try
        {
            var language = profile?.ContentLanguage ?? (text.Language == "tr" ? ContentLanguages.Turkish : ContentLanguages.English);
            await Picker.LoadAsync(profile?.Id, language, PipelineSteps.ConceptPage).ConfigureAwait(true);
            if (newProfile || FocusRows.Count == 0)
            {
                LoadFocus();
            }
        }
        finally
        {
            _loading = false;
        }

        // The first thing to do is in sight: a profile without a concept model gets the models unfolded.
        if (Picker.Model(PipelineSteps.Concepts) is null)
        {
            ModelsOpen = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The chosen focus as the prompt's line in <paramref name="language"/> (empty when nothing is chosen).</summary>
    public string Focus(string language) =>
        FocusText.Build(language, FocusRows.Where(r => r.Chosen is not null).Select(r => (r.Dimension, r.Chosen!)));

    /// <summary>The effort chosen for the concept model now (empty = automatic).</summary>
    public string ConceptEffort() => Picker.Row(PipelineSteps.Concepts)?.Effort?.Value ?? string.Empty;

    /// <summary>The fine-tuning drop-downs, every one on "any".</summary>
    private void LoadFocus()
    {
        foreach (var row in FocusRows)
        {
            row.PropertyChanged -= OnFocusRowChanged;
        }

        FocusRows.Clear();
        foreach (var dimension in focus.Dimensions)
        {
            var options = new List<ChoiceOption> { new(string.Empty, text["focus.none"]) };
            options.AddRange(dimension.Options.Select(o => new ChoiceOption(o.Id, Name(o.Name))));
            var row = new FocusRowViewModel(dimension, Name(dimension.Name), options);
            row.PropertyChanged += OnFocusRowChanged;
            FocusRows.Add(row);
        }

        ShowFineTune();
    }

    partial void OnModelsOpenChanged(bool value)
    {
        if (value)
        {
            FineTuneOpen = false;
        }
    }

    partial void OnFineTuneOpenChanged(bool value)
    {
        if (value)
        {
            ModelsOpen = false;
        }
    }

    private void OnFocusRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ShowFineTune();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ShowFineTune() => FineTuneSummary = string.Join(" · ",
        FocusRows.Select(r => r.Title + ": " + (r.Chosen is null ? text["concepts.finetune.none"] : r.Selected!.Label)));

    private string Name(Core.Templates.LocalizedText name) => text.Language == ContentLanguages.Turkish ? name.Tr : name.En;

    private static StepPickerViewModel Layout(StepPickerViewModel picker)
    {
        picker.Compact = true;
        picker.Columns = 3;
        return picker;
    }
}

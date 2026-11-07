using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Pronunciation;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Voice settings: the pronunciation dictionary of each output language (the built-in words and the user's own; the user's word of the same
/// spelling replaces a built-in one, removing it brings the built-in one back). A word changes only what the voice reads, never the subtitles.
/// </summary>
public sealed partial class VoiceSettingsViewModel(PronunciationService pronunciations, Localizer text, ILogger<VoiceSettingsViewModel> logger)
    : SettingsSectionBase
{
    /// <inheritdoc />
    public override string TitleKey => "settings.voices";

    /// <inheritdoc />
    public override int Order => 30;

    /// <summary>Output languages.</summary>
    public IReadOnlyList<ChoiceOption> Languages => _languages ??= LanguageOptions.For(text);

    private IReadOnlyList<ChoiceOption>? _languages;

    /// <summary>The language whose dictionary is shown.</summary>
    [ObservableProperty]
    public partial ChoiceOption? Language { get; set; }

    /// <summary>The words of <see cref="Language"/>: the user's first, then the built-in ones.</summary>
    public ObservableCollection<PronunciationRow> Words { get; } = [];

    /// <summary>The word to add as written.</summary>
    [ObservableProperty]
    public partial string NewWord { get; set; } = string.Empty;

    /// <summary>How the voice should read it.</summary>
    [ObservableProperty]
    public partial string NewSpoken { get; set; } = string.Empty;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        if (Language is null)
        {
            // Setting the language loads its words (OnLanguageChanged).
            Language = Languages.FirstOrDefault(l => l.Value == LanguageOptions.OfInterface(text)) ?? Languages[0];
            return;
        }

        await LoadWordsAsync().ConfigureAwait(true);
    }

    /// <summary>Adds the word (or changes the user's word of the same spelling).</summary>
    [RelayCommand]
    public async Task AddWordAsync()
    {
        if (Language is not { } language)
        {
            return;
        }

        var problem = await pronunciations.AddAsync(language.Value, NewWord, NewSpoken, CancellationToken.None).ConfigureAwait(true);
        if (problem is not null)
        {
            Status = text["voices.words." + problem];
            return;
        }

        Status = text.Format("voices.words.added", NewWord.Trim());
        NewWord = string.Empty;
        NewSpoken = string.Empty;
        await LoadWordsAsync().ConfigureAwait(true);
    }

    /// <summary>Removes the user's word.</summary>
    [RelayCommand]
    public async Task DeleteWordAsync(PronunciationRow? row)
    {
        if (row?.Entry is not { } entry)
        {
            return;
        }

        await pronunciations.DeleteAsync(entry, CancellationToken.None).ConfigureAwait(true);
        Status = text.Format("voices.words.removed", row.Word);
        await LoadWordsAsync().ConfigureAwait(true);
    }

    partial void OnLanguageChanged(ChoiceOption? value) => _ = LoadWordsSafelyAsync();

    private async Task LoadWordsSafelyAsync()
    {
        try
        {
            await LoadWordsAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The pronunciation list could not be loaded");
            Status = text.Format("page.load_failed", ex.Message);
        }
    }

    private async Task LoadWordsAsync()
    {
        if (Language is not { } language)
        {
            return;
        }

        var rules = await pronunciations.RulesAsync(language.Value, CancellationToken.None).ConfigureAwait(true);
        Words.Clear();
        foreach (var rule in rules)
        {
            Words.Add(new PronunciationRow(rule.Word, rule.Spoken, rule.Entry, text[rule.BuiltIn ? "voices.words.builtin" : "voices.words.own"]));
        }
    }
}

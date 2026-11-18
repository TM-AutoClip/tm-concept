using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Globalization;
using TMConcept.Core.Models;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Pronunciation;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Voice settings: the pronunciation dictionary of each output language (the built-in words with their categories and the user's own; the
/// user's word of the same spelling replaces a built-in one, removing it brings the built-in one back), searched by word or reading, a word
/// edited by putting it back into the boxes (a built-in one becomes the user's own), and listened to with an installed local voice of the
/// language before or after it is saved. A word changes only what the voice reads, never the subtitles.
/// The user's own voices for the voice that copies a voice (Pocket TTS): added from a recording (ffmpeg converts it; installed after a
/// confirmation when missing), played, removed. No consent step (rule 8); the tab says whose voice is the user's responsibility.
/// </summary>
public sealed partial class VoiceSettingsViewModel(PronunciationService pronunciations, VoiceSampleLibrary samples, ComponentInstallFlow installs,
    FileDialogs dialogs, IShellLauncher shell, IAppPaths paths, Localizer text, SpeechService speech, IModelCatalog catalog,
    ILogger<VoiceSettingsViewModel> logger)
    : SettingsSectionBase
{
    private IReadOnlyList<PronunciationRow> _all = [];
    private PronunciationRow? _editing;

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

    /// <summary>The words of <see cref="Language"/> that match <see cref="Search"/>: the user's first, then the built-in ones.</summary>
    public ObservableCollection<PronunciationRow> Words { get; } = [];

    /// <summary>Filters the list by the word, its reading or its category.</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>How many built-in and own words the language has.</summary>
    [ObservableProperty]
    public partial string Counts { get; set; } = string.Empty;

    /// <summary>The add button's text: "Add", or "Save" while a word is being edited.</summary>
    [ObservableProperty]
    public partial string AddLabel { get; set; } = string.Empty;

    /// <summary>The installed local voices of <see cref="Language"/> the listen button can use.</summary>
    public ObservableCollection<ChoiceOption> ListenVoices { get; } = [];

    /// <summary>The voice that listens.</summary>
    [ObservableProperty]
    public partial ChoiceOption? ListenVoice { get; set; }

    /// <summary>The sentence to listen to (empty: the word in the box).</summary>
    [ObservableProperty]
    public partial string ListenText { get; set; } = string.Empty;

    /// <summary>A listen is being spoken.</summary>
    [ObservableProperty]
    public partial bool Listening { get; set; }

    /// <summary>The word to add as written.</summary>
    [ObservableProperty]
    public partial string NewWord { get; set; } = string.Empty;

    /// <summary>How the voice should read it.</summary>
    [ObservableProperty]
    public partial string NewSpoken { get; set; } = string.Empty;

    /// <summary>The user's voice samples.</summary>
    public ObservableCollection<VoiceSampleInfo> Samples { get; } = [];

    /// <summary>The name of the next voice sample.</summary>
    [ObservableProperty]
    public partial string SampleName { get; set; } = string.Empty;

    /// <summary>A recording is being added (ffmpeg may be installed first).</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>What happened to the last voice sample action.</summary>
    [ObservableProperty]
    public partial string SampleStatus { get; set; } = string.Empty;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        await LoadSamplesAsync().ConfigureAwait(true);
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

        // An own word edited under another spelling: the old spelling goes.
        var culture = OutputLanguages.Of(language.Value).Culture;
        if (_editing?.Entry is { } old && PronunciationRewriter.Fold(old.Word.Trim(), culture) != PronunciationRewriter.Fold(NewWord.Trim(), culture))
        {
            await pronunciations.DeleteAsync(old, CancellationToken.None).ConfigureAwait(true);
        }

        Status = text.Format("voices.words.added", NewWord.Trim());
        NewWord = string.Empty;
        NewSpoken = string.Empty;
        _editing = null;
        AddLabel = text["voices.words.add"];
        await LoadWordsAsync().ConfigureAwait(true);
    }

    /// <summary>Puts a word into the boxes to change it (a built-in word becomes the user's own when saved).</summary>
    [RelayCommand]
    public void EditWord(PronunciationRow? row)
    {
        if (row is null)
        {
            return;
        }

        _editing = row;
        NewWord = row.Shown;
        NewSpoken = row.Spoken;
        AddLabel = text["voices.words.save"];
        Status = text.Format("voices.words.editing", row.Shown);
    }

    /// <summary>
    /// Speaks <see cref="ListenText"/> (or the word in the box) with <see cref="ListenVoice"/> and opens it; the word and reading in the
    /// boxes apply even before they are saved.
    /// </summary>
    [RelayCommand]
    public async Task ListenAsync()
    {
        if (Listening || Language is not { } language)
        {
            return;
        }

        var sentence = ListenText.Trim().Length > 0 ? ListenText.Trim() : NewWord.Trim().TrimStart(PronunciationRule.ExactMark);
        var engine = speech.Engines.FirstOrDefault(e => e.IsLocal && e.Code == ListenVoice?.Value);
        var model = catalog.All.FirstOrDefault(m => m.Provider == ModelProviders.Local && m.Id == engine?.Code);
        if (engine is null || model is null)
        {
            Status = text["voices.listen.no_voice"];
            return;
        }

        if (sentence.Length == 0)
        {
            Status = text["voices.listen.empty"];
            return;
        }

        IReadOnlyList<PronunciationRule> extra = [];
        if (NewWord.Trim().Length > 0 && NewSpoken.Trim().Length > 0)
        {
            extra = [PronunciationService.Own(new PronunciationEntry { Language = language.Value, Word = NewWord, Spoken = NewSpoken })];
        }

        Listening = true;
        try
        {
            var setup = new SpeechSetup(engine, model, null, string.Empty, 1.0);
            var wav = Path.Combine(paths.WorkDirectory, "listen", "pronunciation.wav");
            await speech.SynthesizeAsync(setup, language.Value, sentence, wav, extra, CancellationToken.None).ConfigureAwait(true);
            shell.OpenPath(wav);
        }
        catch (Exception ex) when (ex is SpeechException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Listening to a pronunciation failed");
            Status = text.Format("speech.failed", ex.Message);
        }
        finally
        {
            Listening = false;
        }
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

    /// <summary>Asks for a recording and adds it under <see cref="SampleName"/>.</summary>
    [RelayCommand]
    public async Task AddSampleAsync()
    {
        if (SampleName.Trim().Length == 0)
        {
            SampleStatus = text["voices.samples.problem.name"];
            return;
        }

        var file = await dialogs.OpenAsync("voices.samples.dialog", "voices.samples.filter", VoiceSampleLimits.Patterns).ConfigureAwait(true);
        if (file is not null)
        {
            await ImportAsync(file).ConfigureAwait(true);
        }
    }

    /// <summary>Adds <paramref name="file"/> as a voice sample named <see cref="SampleName"/> (ffmpeg is installed first when missing, after asking).</summary>
    public async Task ImportAsync(string file)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            var outcome = await installs.EnsureAsync(samples.RequiredComponents, "install.purpose.voice_sample", new Progress<ToolProgress>(), CancellationToken.None)
                .ConfigureAwait(true);
            if (!outcome.Ready)
            {
                SampleStatus = outcome.Reason;
                return;
            }

            var result = await samples.ImportAsync(file, SampleName, CancellationToken.None).ConfigureAwait(true);
            if (result.Sample is not { } sample)
            {
                SampleStatus = text["voices.samples.problem." + result.Problem];
                return;
            }

            SampleStatus = text.Format("voices.samples.added", sample.Name, (sample.DurationMs / 1000.0).ToString("0", CultureInfo.InvariantCulture));
            SampleName = string.Empty;
            await LoadSamplesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ToolInstallException)
        {
            logger.LogWarning(ex, "A voice sample could not be added");
            SampleStatus = text["voices.samples.problem.read"];
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Opens the recording of a sample in the system player (from a work file).</summary>
    [RelayCommand]
    public async Task PlaySampleAsync(VoiceSampleInfo? sample)
    {
        if (sample is null)
        {
            return;
        }

        try
        {
            var work = await samples.WriteAsync(sample.Id, CancellationToken.None).ConfigureAwait(true);
            var listen = Path.Combine(paths.WorkDirectory, "listen", "voice-sample.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(listen)!);
            File.Move(work, listen, overwrite: true);
            shell.OpenPath(listen);
        }
        catch (Exception ex) when (ex is SpeechException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "A voice sample could not be played");
            SampleStatus = ex.Message;
        }
    }

    /// <summary>
    /// Removes a sample (a voice choice that names it falls back to another voice on the video page). A file of it that is in use, or a
    /// storage error, keeps the sample and the reason is shown.
    /// </summary>
    [RelayCommand]
    public async Task DeleteSampleAsync(VoiceSampleInfo? sample)
    {
        if (sample is null)
        {
            return;
        }

        try
        {
            await samples.DeleteAsync(sample.Id, CancellationToken.None).ConfigureAwait(true);
            SampleStatus = text.Format("voices.samples.removed", sample.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "A voice sample could not be removed");
            SampleStatus = text.Format("voices.samples.remove_failed", sample.Name, ex.Message);
        }

        await LoadSamplesAsync().ConfigureAwait(true);
    }

    private async Task LoadSamplesAsync()
    {
        await samples.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        Samples.Clear();
        foreach (var sample in samples.Samples)
        {
            Samples.Add(sample);
        }
    }

    partial void OnLanguageChanged(ChoiceOption? value) => _ = LoadWordsSafelyAsync();

    partial void OnSearchChanged(string value) => Filter();

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

        if (AddLabel.Length == 0)
        {
            AddLabel = text["voices.words.add"];
        }

        var rules = await pronunciations.RulesAsync(language.Value, CancellationToken.None).ConfigureAwait(true);
        _all = rules.Select(rule => new PronunciationRow(rule.Word, rule.Spoken, rule.Entry, Tag(rule), rule.ExactCase, rule.Category)).ToList();
        Counts = text.Format("voices.words.counts", _all.Count(r => !r.Own), _all.Count(r => r.Own));
        Filter();

        // The local voices of the language that are installed (the listen button never downloads anything).
        var current = ListenVoice?.Value;
        ListenVoices.Clear();
        foreach (var engine in speech.Engines.Where(e => e.IsLocal && e.Ready && e.Supports(language.Value)))
        {
            ListenVoices.Add(new ChoiceOption(engine.Code, engine.Name));
        }

        ListenVoice = ListenVoices.FirstOrDefault(v => v.Value == current) ?? ListenVoices.FirstOrDefault();
    }

    private string Tag(PronunciationRule rule)
    {
        if (!rule.BuiltIn)
        {
            return text["voices.words.own"];
        }

        return rule.Category.Length > 0 ? text["voices.words.builtin"] + " \u00b7 " + rule.Category : text["voices.words.builtin"];
    }

    private void Filter()
    {
        var search = Search.Trim();
        var culture = Language is { } language ? OutputLanguages.Of(language.Value).Culture : CultureInfo.InvariantCulture;
        var folded = PronunciationRewriter.Fold(search, culture);
        Words.Clear();
        foreach (var row in _all.Where(r => search.Length == 0
            || PronunciationRewriter.Fold(r.Word, culture).Contains(folded, StringComparison.Ordinal)
            || PronunciationRewriter.Fold(r.Spoken, culture).Contains(folded, StringComparison.Ordinal)
            || PronunciationRewriter.Fold(r.Category, culture).Contains(folded, StringComparison.Ordinal)))
        {
            Words.Add(row);
        }
    }
}

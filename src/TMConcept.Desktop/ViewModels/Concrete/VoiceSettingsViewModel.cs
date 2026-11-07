using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Globalization;
using TMConcept.Core.Paths;
using TMConcept.Core.Pronunciation;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Voice settings: the pronunciation dictionary of each output language (the built-in words and the user's own; the user's word of the same
/// spelling replaces a built-in one, removing it brings the built-in one back). A word changes only what the voice reads, never the subtitles.
/// The user's own voices for the voice that copies a voice (Pocket TTS): added from a recording (ffmpeg converts it; installed after a
/// confirmation when missing), played, removed. No consent step (rule 8); the tab says whose voice is the user's responsibility.
/// </summary>
public sealed partial class VoiceSettingsViewModel(PronunciationService pronunciations, VoiceSampleLibrary samples, ComponentInstallFlow installs,
    FileDialogs dialogs, ShellLauncher shell, IAppPaths paths, Localizer text, ILogger<VoiceSettingsViewModel> logger)
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

    /// <summary>Removes a sample (a voice choice that names it falls back to another voice on the video page).</summary>
    [RelayCommand]
    public async Task DeleteSampleAsync(VoiceSampleInfo? sample)
    {
        if (sample is null)
        {
            return;
        }

        await samples.DeleteAsync(sample.Id, CancellationToken.None).ConfigureAwait(true);
        SampleStatus = text.Format("voices.samples.removed", sample.Name);
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

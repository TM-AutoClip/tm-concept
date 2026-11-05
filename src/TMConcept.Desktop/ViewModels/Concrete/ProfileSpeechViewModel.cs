using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The narration part of a profile: off (silent video), local Supertonic 3 (default, free), local Piper Turkish (non-commercial only), or
/// the online model of the speech step. Voice and speed; "Listen" speaks a sample sentence.
/// </summary>
public sealed partial class ProfileSpeechViewModel(SpeechService speech, IAppPaths paths, ShellLauncher shell, Localizer text,
    ILogger<ProfileSpeechViewModel> logger) : ObservableObject
{
    private string _language = ContentLanguages.English;

    /// <summary>Choices.</summary>
    public ObservableCollection<ChoiceOption> Engines { get; } = [];

    /// <summary>Chosen engine.</summary>
    [ObservableProperty]
    public partial ChoiceOption? Engine { get; set; }

    /// <summary>Voices of the chosen engine.</summary>
    public ObservableCollection<ChoiceOption> Voices { get; } = [];

    /// <summary>Chosen voice.</summary>
    [ObservableProperty]
    public partial ChoiceOption? Voice { get; set; }

    /// <summary>Speed.</summary>
    [ObservableProperty]
    public partial decimal? Speed { get; set; } = 1.0m;

    /// <summary>Note under the choice (license, installation).</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Busy.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Shows a profile's choice.</summary>
    public void Load(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _language = profile.ContentLanguage;
        Engines.Clear();
        Engines.Add(new ChoiceOption(SpeechEngines.Off, text["speech.off"]));
        foreach (var engine in speech.Engines.Where(e => e.IsLocal && e.Supports(_language)))
        {
            Engines.Add(new ChoiceOption(engine.Code, text.Format("speech.local", engine.Name)));
        }

        Engines.Add(new ChoiceOption(SpeechEngines.Online, text["speech.online"]));
        Engine = Engines.FirstOrDefault(e => e.Value == profile.SpeechEngine) ?? Engines.FirstOrDefault(e => e.Value == SpeechEngines.Supertonic) ?? Engines[0];
        Voice = Voices.FirstOrDefault(v => v.Value == profile.SpeechVoice) ?? Voices.FirstOrDefault();
        Speed = (decimal)Math.Clamp(profile.SpeechSpeed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax);
    }

    /// <summary>Writes the choice into a profile.</summary>
    public void Apply(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.SpeechEngine = Engine?.Value ?? SpeechEngines.Off;
        profile.SpeechVoice = Voice?.Value ?? string.Empty;
        profile.SpeechSpeed = (double)Math.Clamp(Speed ?? 1, (decimal)ProfileLimits.SpeedMin, (decimal)ProfileLimits.SpeedMax);
    }

    /// <summary>Speaks a sample sentence and opens it.</summary>
    [RelayCommand]
    public async Task ListenAsync()
    {
        if (Engine is null || Engine.Value == SpeechEngines.Off)
        {
            return;
        }

        Busy = true;
        try
        {
            var wav = Path.Combine(paths.WorkDirectory, "listen", "sample.wav");
            await speech.SynthesizeAsync(Engine.Value, Voice?.Value ?? string.Empty, (double)(Speed ?? 1), _language,
                text[_language == ContentLanguages.Turkish ? "speech.sample.tr" : "speech.sample.en"], wav, CancellationToken.None).ConfigureAwait(true);
            shell.OpenPath(wav);
            Note = string.Empty;
        }
        catch (SpeechException ex)
        {
            logger.LogWarning(ex, "Listen failed ({Code})", ex.Code);
            Note = text.Format("speech.failed", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Core.Providers.ProviderException)
        {
            logger.LogWarning(ex, "Listen failed");
            Note = text.Format("speech.failed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    partial void OnEngineChanged(ChoiceOption? value)
    {
        Voices.Clear();
        Note = string.Empty;
        if (value is null || value.Value == SpeechEngines.Off)
        {
            Note = text["speech.off.note"];
            return;
        }

        if (value.Value == SpeechEngines.Online)
        {
            foreach (var engine in speech.Engines.Where(e => !e.IsLocal))
            {
                foreach (var voice in engine.Voices(_language))
                {
                    Voices.Add(new ChoiceOption(voice.Id, engine.Name + " · " + voice.Label));
                }
            }

            Note = text["speech.online.note"];
        }
        else if (speech.Engines.FirstOrDefault(e => e.Code == value.Value) is { } local)
        {
            foreach (var voice in local.Voices(_language))
            {
                Voices.Add(new ChoiceOption(voice.Id, voice.Label));
            }

            Note = (local.Ready ? string.Empty : text["speech.not_installed"] + " ")
                + (local.Code == SpeechEngines.PiperTurkish ? text["speech.piper.note"] : text["speech.supertonic.note"]);
        }

        Voice = Voices.FirstOrDefault();
    }
}

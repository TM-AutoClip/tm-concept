using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Components;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The "Müzik üret" page (0.1.54, next to Konseptler and Konseptten video): background music made from the user's own words ("Mısır'ın
/// gizemi" → antik Mısır müziği, "antik Mısır'da disko" → diskoyla karışık; the prompt is free) and, when wanted, the tempo of a reference
/// sound, with the music models the videos use (local and online, with their prices). "Üret" works like every other page (0.1.55; 0.1.54 had
/// its own "download first" button): the chosen model is used when it is installed; otherwise its licence (when not plainly commercial), an
/// online model's key and ONE confirmation listing its downloads (with ffmpeg for a reference sound) are asked, then it is installed and the
/// piece is made (<see cref="IModelStartGate"/>). Each piece goes into "Müziklerim", which also takes the user's own audio files; the video
/// page's music choice picks from it. Making a piece is page work (the profile cannot be switched meanwhile, closing asks); Cancel stops it.
/// Choosing a model per video in a batch is not offered (the developer: not now).
/// </summary>
public sealed partial class MusicViewModel(IVideoMediaServices services, IMusicMaker maker, IMyMusic myMusic, IModelStartGate gate, IMusicChoiceText labels,
    IActivityLog activity, IConfirmDialog confirm, Os.IShellLauncher shell, Localizer text, ILogger<MusicViewModel> logger) : PageViewModelBase
{
    private CancellationTokenSource? _work;
    private MusicGeneratorServices? _generators;

    /// <inheritdoc />
    public override string TitleKey => "nav.music";

    /// <inheritdoc />
    public override int Order => 25;

    /// <summary>What the music should be like (free words).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    public partial string Prompt { get; set; } = string.Empty;

    /// <summary>The piece's name in "Müziklerim" (empty = the prompt's first words).</summary>
    [ObservableProperty]
    public partial string PieceTitle { get; set; } = string.Empty;

    /// <summary>The length asked for (seconds; a model with a fixed length ignores it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimateText))]
    public partial decimal? Seconds { get; set; } = MusicGenerationRules.DefaultSeconds;

    /// <summary>The shortest length offered (seconds).</summary>
    public int MinSeconds => MusicGenerationRules.MinSeconds;

    /// <summary>The longest length offered (seconds).</summary>
    public int MaxSeconds => MusicGenerationRules.MaxSeconds;

    /// <summary>The most characters of a prompt.</summary>
    public int MaxPromptCharacters => MusicGenerationRules.MaxPromptCharacters;

    /// <summary>Without words (the default: it lies under a narration).</summary>
    [ObservableProperty]
    public partial bool Instrumental { get; set; } = true;

    /// <summary>The reference sound (full path; empty = none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReferenceName), nameof(HasReference))]
    public partial string ReferenceFile { get; set; } = string.Empty;

    /// <summary>The reference sound's name, or "none".</summary>
    public string ReferenceName => ReferenceFile.Length > 0 ? Path.GetFileName(ReferenceFile) : text["music.reference.none"];

    /// <summary>A reference sound is chosen.</summary>
    public bool HasReference => ReferenceFile.Length > 0;

    /// <summary>The music models: the local ones (with their install state) and the online ones with their prices.</summary>
    public ObservableCollection<MusicModelChoice> Models { get; } = [];

    /// <summary>The chosen model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimateText), nameof(CardWarning))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    public partial MusicModelChoice? Model { get; set; }

    /// <summary>The chosen model's yellow note on this computer (a card it is not made for, Linux); empty otherwise. Never a block.</summary>
    public string CardWarning => _generators is { } g ? labels.CardWarning(Model?.Model, g.Card) : string.Empty;

    /// <summary>"About 0.08 $ for 120 s" (the local model: free).</summary>
    public string EstimateText
    {
        get
        {
            if (Model is not { } choice)
            {
                return string.Empty;
            }

            var (usd, seconds) = maker.Estimate(choice.Model, (int)(Seconds ?? MusicGenerationRules.DefaultSeconds));
            return usd == 0 ? text.Format("music.estimate.free", seconds) : text.Format("music.estimate", text.Usd(usd), seconds);
        }
    }

    /// <summary>A piece is being made (Cancel is shown).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Idle))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(CancelCommand), nameof(AddFileCommand))]
    public partial bool Busy { get; set; }

    /// <summary>Not making a piece.</summary>
    public bool Idle => !Busy;

    /// <summary>"Müziklerim", newest first.</summary>
    public ObservableCollection<MyMusicRow> Pieces { get; } = [];

    /// <summary>"Müziklerim" has nothing yet.</summary>
    [ObservableProperty]
    public partial bool Empty { get; set; } = true;

    /// <inheritdoc />
    public override async Task ActivateAsync()
    {
        _generators ??= await services.GeneratorsAsync().ConfigureAwait(true);
        LoadModels();
        await LoadPiecesAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override void CancelWork() => _work?.Cancel();

    /// <summary>Asks for a reference sound (its tempo is measured when the piece is made).</summary>
    [RelayCommand]
    public async Task PickReferenceAsync()
    {
        if (await services.PickMusicFileAsync().ConfigureAwait(true) is { Length: > 0 } file)
        {
            ReferenceFile = file;
        }
    }

    /// <summary>Forgets the reference sound.</summary>
    [RelayCommand]
    public void ClearReference() => ReferenceFile = string.Empty;

    /// <summary>
    /// Makes the piece and adds it to "Müziklerim". First the chosen model is made ready the way every page does it: used as it is when
    /// installed; otherwise its licence, an online model's key and one confirmation of its downloads (ffmpeg too for a reference sound), then
    /// the install. Declined or failed: nothing is made, the reason is shown.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    public async Task GenerateAsync()
    {
        if (Model is not { } choice)
        {
            return;
        }

        _work?.Dispose();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        Busy = true;
        Working = true;
        Status = string.Empty;
        var progress = new Progress<string>(stage => Status = text["music.stage." + stage]);
        try
        {
            var components = HasReference ? new[] { ComponentCodes.Ffmpeg } : [];
            var ready = await gate.PrepareAsync([choice.Model], components, "install.purpose.music_make", null, token).ConfigureAwait(true);
            if (!ready.Ready)
            {
                logger.LogInformation("Music not made: {Model} is not ready ({Reason})", ModelBase.KeyOf(choice.Model), ready.Reason);
                Status = ready.Reason;
                activity.Add(ActivityLevel.Warning, ready.Reason);
                return;
            }

            if (!choice.Ready)
            {
                // Installed just now: the list no longer says "not installed".
                LoadModels();
            }

            Status = text["music.stage.generate"];
            var request = new MusicMakeRequest(choice.Model, Prompt, (int)(Seconds ?? MusicGenerationRules.DefaultSeconds), Instrumental,
                HasReference ? ReferenceFile : null, PieceTitle);
            var entry = await Task.Run(() => maker.MakeAsync(request, progress, token), token).ConfigureAwait(true);
            Status = entry.Usd > 0 ? text.Format("music.made.paid", entry.Track.Title, text.Usd(entry.Usd)) : text.Format("music.made", entry.Track.Title);
            activity.Add(ActivityLevel.Info, Status);
            PieceTitle = string.Empty;
            await LoadPiecesAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = text["music.cancelled"];
        }
        catch (Exception ex) when (ex is MusicGenerationException or ToolInstallException or IOException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or System.ComponentModel.Win32Exception)
        {
            // Every failure of the model comes as a MusicGenerationException (the shared runner names stray ones). The rest happen around it:
            // the install (a download, a failed tool), the key test, the reference sound's ffmpeg not starting, the run not saved, the files.
            logger.LogError(Core.Activity.LogEvents.OnScreen, ex, "The music could not be made by {Model}", choice.Model.Name);
            Status = text.Format("music.failed", ex.Message);
            activity.Add(ActivityLevel.Error, Status);
        }
        finally
        {
            Busy = false;
            Working = false;
        }
    }

    /// <summary>Stops the piece being made.</summary>
    [RelayCommand(CanExecute = nameof(Busy))]
    public void Cancel() => _work?.Cancel();

    /// <summary>Adds the user's own audio file to "Müziklerim" (a copy; its licence is the user's).</summary>
    [RelayCommand(CanExecute = nameof(Idle))]
    public async Task AddFileAsync()
    {
        if (await services.PickMusicFileAsync().ConfigureAwait(true) is not { Length: > 0 } file)
        {
            return;
        }

        try
        {
            var entry = await Task.Run(() => myMusic.AddFileAsync(file, CancellationToken.None)).ConfigureAwait(true);
            Status = text.Format("music.added", entry.Track.Title);
            await LoadPiecesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(Core.Activity.LogEvents.OnScreen, ex, "A music file could not be added to My music");
            Status = text.Format("music.add_failed", ex.Message);
            activity.Add(ActivityLevel.Error, Status);
        }
    }

    /// <summary>Opens a piece in the computer's player.</summary>
    [RelayCommand]
    public void Play(MyMusicRow? row)
    {
        if (row is not null && File.Exists(row.Entry.Track.File))
        {
            shell.OpenPath(row.Entry.Track.File);
        }
    }

    /// <summary>Removes a piece after a confirmation (its file is deleted for good; a made video keeps its own copy).</summary>
    [RelayCommand]
    public async Task RemoveAsync(MyMusicRow? row)
    {
        if (row is null || Busy)
        {
            return;
        }

        var answer = await confirm.AskAsync(text["music.remove.title"], text.Format("music.remove.message", row.Title), text["common.delete"], danger: true)
            .ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            await Task.Run(() => myMusic.RemoveAsync(row.Entry.Track.Id, CancellationToken.None)).ConfigureAwait(true);
            Status = text.Format("music.removed", row.Title);
            await LoadPiecesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(Core.Activity.LogEvents.OnScreen, ex, "A piece of My music could not be removed");
            Status = text.Format("music.remove_failed", ex.Message);
            activity.Add(ActivityLevel.Error, Status);
        }
    }

    private bool CanGenerate() => !Busy && Model is not null && !string.IsNullOrWhiteSpace(Prompt);

    private void LoadModels()
    {
        if (_generators is null)
        {
            return;
        }

        var chosen = Model?.Key;
        Models.Clear();
        foreach (var model in _generators.Models)
        {
            var ready = _generators.Ready(model);
            Models.Add(new MusicModelChoice(ModelBase.KeyOf(model), labels.ModelLabel(model, ready, _generators.Card), model,
                model.Provider == ModelProviders.Local, ready));
        }

        Model = Models.FirstOrDefault(m => m.Key == chosen) ?? Models.FirstOrDefault(m => m.Ready) ?? Models.FirstOrDefault();
        OnPropertyChanged(nameof(CardWarning));
    }

    private async Task LoadPiecesAsync()
    {
        IReadOnlyList<MyMusicEntry> entries;
        try
        {
            entries = await Task.Run(() => myMusic.ListAsync(CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "My music could not be read");
            entries = [];
        }

        Pieces.Clear();
        foreach (var entry in entries)
        {
            Pieces.Add(new MyMusicRow(entry, labels.PieceDetail(entry, full: true)));
        }

        Empty = Pieces.Count == 0;
    }
}

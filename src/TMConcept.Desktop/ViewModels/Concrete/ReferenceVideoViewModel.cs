using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Core.Activity;
using TMConcept.Core.References;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The profile's reference video ("bu videonun temposunda yap", S1, 0.1.52; 0.1.54: per profile on the video page's model choice, no longer
/// in the template): "Dosya seç" measures a video file, "Adres yapıştır" + "Ölç" downloads one video with yt-dlp (installed after one
/// confirmation) and measures it. The measure runs off the window's thread with its stage and a Cancel; only numbers are kept (the download is
/// deleted). Each video takes the measured tempo, cut rhythm and colours when it starts. A failure is shown here and in the activity panel
/// and never touches a video.
/// </summary>
public sealed partial class ReferenceVideoViewModel : ObservableObject, IDisposable
{
    private readonly ReferenceVideoServices _services;
    private readonly Localizer _text;
    private CancellationTokenSource? _work;

    /// <summary>Shows <paramref name="profile"/> (the profile's reference, or null).</summary>
    public ReferenceVideoViewModel(ReferenceVideoServices services, Localizer text, ReferenceTempoProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(text);
        _services = services;
        _text = text;
        Profile = profile;
    }

    /// <summary>The measured reference (null = none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfile), nameof(SourceText), nameof(TempoText), nameof(ColorText), nameof(SoundText), nameof(HasPalette))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial ReferenceTempoProfile? Profile { get; set; }

    /// <summary>A reference is measured.</summary>
    public bool HasProfile => Profile is not null;

    /// <summary>"Reference: clip.mp4 (measured 2 min 10 s)".</summary>
    public string SourceText => Profile is { } p
        ? _text.Format("styles.reference.source", p.Source, TimeSpan.FromSeconds(Math.Round(p.Seconds)).ToString(@"m\:ss", _text.Culture))
        : string.Empty;

    /// <summary>"4.2 cuts in 10 s · shot 2.4 s · camera on 45 % of shots (mostly left) · tempo: Tight".</summary>
    public string TempoText
    {
        get
        {
            if (Profile is not { } p)
            {
                return string.Empty;
            }

            var suggestion = ReferenceTempoMapper.Suggest(p);
            var direction = p.CameraDirection == CameraDirection.None ? string.Empty
                : _text.Format("styles.reference.direction", _text["styles.reference.direction." + p.CameraDirection.ToString().ToLowerInvariant()]);
            return _text.Format("styles.reference.tempo_line", p.CutsPer10Seconds.ToString("0.#", _text.Culture), p.AverageShotSeconds.ToString("0.#", _text.Culture),
                Math.Round(p.CameraShare * 100).ToString("0", _text.Culture), direction, _text["style.tempo." + suggestion.Tempo.ToString().ToLowerInvariant()]);
        }
    }

    /// <summary>"Colour: brightness 42 %, saturation 35 %".</summary>
    public string ColorText => Profile is { } p
        ? _text.Format("styles.reference.color_line", Math.Round(p.Brightness * 100).ToString("0", _text.Culture),
            Math.Round(p.Saturation * 100).ToString("0", _text.Culture))
        : string.Empty;

    /// <summary>"Sound: 112 bpm · emphasis at 18 %, 52 %, 87 %" (or "no sound").</summary>
    public string SoundText
    {
        get
        {
            if (Profile is not { } p)
            {
                return string.Empty;
            }

            var peaks = p.Peaks.Count == 0 ? _text["styles.reference.no_peaks"]
                : _text.Format("styles.reference.peaks", string.Join(", ", p.Peaks.Select(s => Math.Round(s * 100).ToString("0", _text.Culture) + " %")));
            if (!p.HasSound)
            {
                return _text["styles.reference.no_sound"] + " · " + peaks;
            }

            var beat = p.Bpm > 0 ? _text.Format("styles.reference.bpm", p.Bpm.ToString("0", _text.Culture)) : _text["styles.reference.no_beat"];
            return beat + " · " + peaks;
        }
    }

    /// <summary>The reference's main colours.</summary>
    public ObservableCollection<IBrush> Swatches { get; } = [];

    /// <summary>The reference has colours to take.</summary>
    public bool HasPalette => Profile is { Palette.Count: > 0 };

    /// <summary>The web address of the video to measure.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MeasureAddressCommand))]
    public partial string Address { get; set; } = string.Empty;

    /// <summary>A measure is running (Cancel is shown).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MeasureFileCommand), nameof(MeasureAddressCommand), nameof(RemoveCommand))]
    public partial bool Busy { get; set; }

    /// <summary>The download's share (0–100) while yt-dlp downloads.</summary>
    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    /// <summary>The stage has no share to show (the frames, the colour, the sound).</summary>
    [ObservableProperty]
    public partial bool ProgressIndeterminate { get; set; } = true;

    /// <summary>What the measure did last ("Downloading… 42 %", an error, "Measured: tempo set to Tight").</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Measures a video file the user picks.</summary>
    [RelayCommand(CanExecute = nameof(Idle))]
    public async Task MeasureFileAsync()
    {
        if (await _services.PickFileAsync().ConfigureAwait(true) is not { Length: > 0 } file)
        {
            return;
        }

        await RunAsync(false, (progress, token) => _services.Measurer.MeasureFileAsync(file, progress, token)).ConfigureAwait(true);
    }

    /// <summary>Downloads (yt-dlp) and measures <see cref="Address"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanMeasureAddress))]
    public async Task MeasureAddressAsync()
    {
        var address = Address.Trim();
        if (ReferenceMeasurer.Address(address) is null)
        {
            Fail(_text["styles.reference.error.address"]);
            return;
        }

        await RunAsync(true, (progress, token) => _services.Measurer.MeasureAddressAsync(address, progress, token)).ConfigureAwait(true);
    }

    /// <summary>Stops the running measure (a download is stopped too; its folder is deleted).</summary>
    [RelayCommand]
    public void Cancel() => _work?.Cancel();

    /// <summary>Removes the profile's reference (the next video uses the template's own tempo and colours).</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    public void Remove()
    {
        Profile = null;
        Swatches.Clear();
        Status = _text["styles.reference.removed"];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Only cancelled here: the running measure still reads its token (Task.Run, the cancel check) and disposes it in its own finally.
        // Disposing it here made a measure stopped by a closed editor fail with ObjectDisposedException (an InvalidOperationException)
        // and write a false error into the activity panel.
        _work?.Cancel();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Profile))
        {
            Swatches.Clear();
            foreach (var colour in Profile?.Palette ?? [])
            {
                if (Color.TryParse(colour, out var parsed))
                {
                    Swatches.Add(new SolidColorBrush(parsed));
                }
            }
        }
    }

    private bool Idle() => !Busy;

    private bool CanMeasureAddress() => !Busy && Address.Trim().Length > 0;

    private bool CanRemove() => !Busy && Profile is not null;

    private async Task RunAsync(bool address, Func<IProgress<ReferenceProgress>, CancellationToken, Task<ReferenceTempoProfile>> measure)
    {
        _work?.Dispose();
        var work = _work = new CancellationTokenSource();
        Busy = true;
        ProgressValue = 0;
        ProgressIndeterminate = true;
        try
        {
            Status = _text["styles.reference.preparing"];
            if (!await _services.EnsureAsync(address).ConfigureAwait(true))
            {
                Status = _text[address ? "styles.reference.not_installed.address" : "styles.reference.not_installed"];
                return;
            }

            // Created on the window's thread: every report comes back to it.
            var progress = new Progress<ReferenceProgress>(Show);
            var profile = await Task.Run(() => measure(progress, work.Token), work.Token).ConfigureAwait(true);
            Profile = profile;
            var suggestion = ReferenceTempoMapper.Suggest(profile);
            Status = _text.Format("styles.reference.measured", _text["style.tempo." + suggestion.Tempo.ToString().ToLowerInvariant()]);
            _services.Activity.Add(ActivityLevel.Info, _text.Format("styles.reference.measured_log", profile.CutsPer10Seconds.ToString("0.#", _text.Culture)));
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            Status = _text["styles.reference.cancelled"];
        }
        catch (ReferenceMeasureException ex)
        {
            var message = _text["styles.reference.error." + ex.Code];
            Fail(ex.Detail.Length > 0 ? message + " (" + ex.Detail + ")" : message);
        }
        catch (Exception ex) when (ex is Core.Tools.ToolInstallException or IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Fail(_text.Format("styles.reference.error.other", ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_work, work))
            {
                _work = null;
            }

            work.Dispose();
            Busy = false;
            ProgressIndeterminate = true;
        }
    }

    private void Show(ReferenceProgress progress)
    {
        if (!Busy)
        {
            return;
        }

        var stage = _text["styles.reference.stage." + progress.Stage];
        ProgressIndeterminate = progress.Stage != ReferenceStages.Download;
        ProgressValue = Math.Round(progress.Fraction * 100);
        Status = progress.Stage == ReferenceStages.Download ? stage + " " + ProgressValue.ToString("0", _text.Culture) + " %" : stage;
    }

    private void Fail(string message)
    {
        Status = message;
        _services.Activity.Add(ActivityLevel.Error, _text["styles.reference.error.title"] + ": " + message);
    }
}

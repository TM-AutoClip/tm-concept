using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Playback;
using TMConcept.Core.Tools;
using TMConcept.Engines.Playback;
using TMConcept.Engines.Tools;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The in-app player of Produced Videos: a chosen video shows its thumbnail and does not start by itself; ▶/⏸, a seek bar, the time,
/// the volume and full screen. Picture and sound come from <see cref="FfmpegPlayback"/> (the app's ffmpeg). Frames are copied into two
/// bitmaps used in turn (the binding sees a new object, nothing is allocated per frame); the position is read ten times a second while
/// playing. Moving the seek bar seeks once the hand rests for a moment (each seek restarts ffmpeg).
/// </summary>
public sealed partial class PlayerViewModel : ObservableObject, IDisposable
{
    /// <summary>How long the seek bar must rest before the seek runs.</summary>
    public static readonly TimeSpan SeekDelay = TimeSpan.FromMilliseconds(150);

    private readonly FfmpegPlayback _playback;
    private readonly ToolInstaller _tools;
    private readonly Localizer _text;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly DispatcherTimer _timer;
    private readonly WriteableBitmap?[] _bitmaps = new WriteableBitmap?[2];
    private PlaybackSource? _source;
    private int _next;
    private bool _pumpPending;
    private bool _reporting;
    private bool _ended;
    private CancellationTokenSource? _seek;

    /// <summary>Creates.</summary>
    public PlayerViewModel(FfmpegPlayback playback, ToolInstaller tools, Localizer text, ILogger<PlayerViewModel> logger)
    {
        _playback = playback;
        _tools = tools;
        _text = text;
        _logger = logger;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Report(_playback.PositionMs);
        _playback.FrameReady += OnFrameReady;
        _playback.Stopped += stop => Dispatcher.UIThread.Post(() => OnStopped(stop));
    }

    /// <summary>A full-screen view of this player is wanted (the view opens or closes it).</summary>
    public event EventHandler? FullscreenRequested;

    /// <summary>Whether a video is loaded.</summary>
    [ObservableProperty]
    public partial bool HasVideo { get; set; }

    /// <summary>Playing now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayGlyph))]
    public partial bool IsPlaying { get; set; }

    /// <summary>▶ or ⏸.</summary>
    public string PlayGlyph => IsPlaying ? "⏸" : "▶";

    /// <summary>The newest frame (null before the first: the poster is shown).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPoster))]
    public partial WriteableBitmap? Frame { get; set; }

    /// <summary>The thumbnail shown before the first frame.</summary>
    [ObservableProperty]
    public partial string Poster { get; set; } = string.Empty;

    /// <summary>Whether the poster is shown.</summary>
    public bool ShowPoster => Frame is null;

    /// <summary>Position (ms), bound two-way to the seek bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial double PositionMs { get; set; }

    /// <summary>Length (ms).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial double DurationMs { get; set; }

    /// <summary>"0:12 / 0:48".</summary>
    public string TimeText => PlaybackTime.Format((long)PositionMs) + " / " + PlaybackTime.Format((long)DurationMs);

    /// <summary>Volume 0–100.</summary>
    [ObservableProperty]
    public partial double Volume { get; set; } = 100;

    /// <summary>A note under the picture (no sound device, ffmpeg missing, a playback error); empty when all is well.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>Loads a video without playing it (the thumbnail shows until ▶).</summary>
    public void Load(string file, string poster, int width, int height, double fps, long durationMs)
    {
        Unload();
        Poster = poster;
        DurationMs = Math.Max(0, durationMs);
        if (!File.Exists(file))
        {
            Message = _text["player.file_missing"];
            return;
        }

        string ffmpeg;
        try
        {
            ffmpeg = _tools.Require(ToolKind.Ffmpeg);
        }
        catch (ToolInstallException ex)
        {
            _logger.LogWarning("The player needs ffmpeg ({Code})", ex.Code);
            Message = _text["player.no_ffmpeg"];
            return;
        }

        var (w, h) = FrameScheduler.Size(width, height);
        _source = new PlaybackSource(ffmpeg, file, w, h, FrameScheduler.Fps(fps), DurationMs > 0 ? (long)DurationMs : long.MaxValue / 4);
        _playback.Open(_source);
        _playback.Volume = Volume / 100;
        HasVideo = true;
    }

    /// <summary>Stops and forgets the video.</summary>
    public void Unload()
    {
        CancelSeek();
        _timer.Stop();
        _playback.Stop();
        _source = null;
        HasVideo = false;
        IsPlaying = false;
        _ended = false;
        Frame = null;
        Message = string.Empty;
        Report(0);
    }

    /// <summary>Pauses when playing (leaving the page).</summary>
    public void Pause()
    {
        if (IsPlaying)
        {
            PlayPause();
        }
    }

    /// <summary>▶ / ⏸.</summary>
    [RelayCommand]
    public void PlayPause()
    {
        if (_source is null)
        {
            return;
        }

        CancelSeek();
        if (IsPlaying)
        {
            Report(_playback.Pause());
            _timer.Stop();
            IsPlaying = false;
            return;
        }

        var from = _ended || PositionMs >= DurationMs - 50 ? 0 : (long)PositionMs;
        _ended = false;
        try
        {
            _playback.Play(from);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "The player could not start");
            Message = _text.Format("player.failed", ex.Message);
            return;
        }

        Message = _playback.HasSound ? string.Empty : _text["player.no_sound"];
        IsPlaying = true;
        _timer.Start();
    }

    /// <summary>Full screen on / off.</summary>
    [RelayCommand]
    public void Fullscreen() => FullscreenRequested?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose()
    {
        CancelSeek();
        _timer.Stop();
        _playback.FrameReady -= OnFrameReady;
        _playback.Stop();
    }

    partial void OnVolumeChanged(double value) => _playback.Volume = value / 100;

    partial void OnPositionMsChanged(double value)
    {
        if (_reporting || _source is null)
        {
            return;
        }

        // The user moved the seek bar: seek when it rests (a drag would otherwise start ffmpeg for every pixel).
        CancelSeek();
        var cancel = _seek = new CancellationTokenSource();
        _ = SeekLaterAsync((long)value, cancel.Token);
    }

    private async Task SeekLaterAsync(long target, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SeekDelay, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        SeekNow(target);
    }

    /// <summary>Seeks at once (also used by tests): playing goes on from there, paused shows the picture there.</summary>
    internal void SeekNow(long target)
    {
        if (_source is null)
        {
            return;
        }

        _ended = false;
        try
        {
            if (IsPlaying)
            {
                _playback.Play(target);
            }
            else
            {
                _playback.ShowFrame(target);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Seeking failed");
            Message = _text.Format("player.failed", ex.Message);
        }

        Report(target);
    }

    private void CancelSeek()
    {
        _seek?.Cancel();
        _seek?.Dispose();
        _seek = null;
    }

    private void Report(long position)
    {
        _reporting = true;
        try
        {
            PositionMs = Math.Clamp(position, 0, Math.Max(0, DurationMs));
        }
        finally
        {
            _reporting = false;
        }
    }

    private void OnStopped(PlaybackStop stop)
    {
        _timer.Stop();
        IsPlaying = false;
        if (stop.Ended)
        {
            _ended = true;
            Report((long)DurationMs);
        }
        else
        {
            Message = _text.Format("player.failed", stop.Error);
            Report(stop.PositionMs);
        }
    }

    private void OnFrameReady()
    {
        lock (_bitmaps)
        {
            if (_pumpPending)
            {
                return;
            }

            _pumpPending = true;
        }

        Dispatcher.UIThread.Post(Pump, DispatcherPriority.Render);
    }

    /// <summary>Copies the newest frame into the bitmap that is not on screen and shows it.</summary>
    private void Pump()
    {
        lock (_bitmaps)
        {
            _pumpPending = false;
        }

        if (_source is not { } source)
        {
            return;
        }

        var bitmap = _bitmaps[_next];
        if (bitmap is null || bitmap.PixelSize.Width != source.Width || bitmap.PixelSize.Height != source.Height)
        {
            bitmap?.Dispose();
            bitmap = _bitmaps[_next] = new WriteableBitmap(new PixelSize(source.Width, source.Height), new Vector(96, 96), PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        bool copied;
        long position;
        using (var frame = bitmap.Lock())
        {
            copied = _playback.CopyLatest(frame.Address, frame.RowBytes, out position);
        }

        if (!copied)
        {
            return;
        }

        _next = 1 - _next;
        Frame = bitmap;
        if (!IsPlaying)
        {
            Report(position);
        }
    }
}

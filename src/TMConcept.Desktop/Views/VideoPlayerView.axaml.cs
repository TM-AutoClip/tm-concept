using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Views;

/// <summary>
/// The player area. Space toggles play; ⛶ opens the same player in a full-screen window (Esc or ⛶ closes it); the full-screen copy only
/// closes itself.
/// </summary>
public sealed partial class VideoPlayerView : UserControl
{
    private PlayerViewModel? _player;
    private PlayerWindow? _fullscreen;

    /// <summary>Creates.</summary>
    public VideoPlayerView()
    {
        InitializeComponent();
        Focusable = true;

        // The seek bar: a left press (on the thumb or the track, which moves the thumb there) holds the position updates, releasing seeks
        // once. Other buttons do not start a seek. handledEventsToo: the slider and its thumb handle these events themselves.
        var seek = this.FindControl<Slider>("SeekBar")!;
        seek.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(seek).Properties.IsLeftButtonPressed)
            {
                _player?.BeginSeek();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        seek.AddHandler(PointerReleasedEvent, (_, _) => _player?.EndSeek(), RoutingStrategies.Bubble, handledEventsToo: true);
        seek.AddHandler(PointerCaptureLostEvent, (_, _) => _player?.EndSeek(), RoutingStrategies.Direct | RoutingStrategies.Bubble,
            handledEventsToo: true);
        DataContextChanged += (_, _) => Attach(DataContext as PlayerViewModel);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && _player is { HasVideo: true } player)
            {
                player.PlayPause();
                e.Handled = true;
            }
        };
    }

    /// <summary>Whether this view is the full-screen copy (its ⛶ closes its window).</summary>
    public bool InFullscreen { get; set; }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Attach(DataContext as PlayerViewModel);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Attach(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Attach(PlayerViewModel? player)
    {
        if (_player is not null)
        {
            _player.FullscreenRequested -= OnFullscreen;
        }

        _player = player;
        if (player is not null && !InFullscreen)
        {
            player.FullscreenRequested += OnFullscreen;
        }
    }

    private void OnFullscreen(object? sender, EventArgs e)
    {
        if (_fullscreen is not null)
        {
            _fullscreen.Close();
            return;
        }

        _fullscreen = new PlayerWindow { DataContext = _player };
        _fullscreen.Closed += (_, _) => _fullscreen = null;
        _fullscreen.Show();
    }
}

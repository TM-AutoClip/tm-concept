using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Music;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The bar of a piece being made on the music page (0.1.61, plan K14/K15): a local model that counts fills it with the running stage's share
/// and reads "Aşama 2/3 · blok 16/40 · %40 · 3:12"; a model that counts nothing (an online model, ACE-Step) or a stage without a count keeps
/// it moving without a share, with the time so far. The time counts on by itself once a second.
/// </summary>
public sealed partial class MusicProgressViewModel(IMusicProgressText labels) : ObservableObject
{
    private readonly Stopwatch _watch = new();
    private DispatcherTimer? _timer;
    private MusicProgress _last = MusicProgress.Unknown;

    /// <summary>A piece is being made (the bar is shown).</summary>
    [ObservableProperty]
    public partial bool Active { get; private set; }

    /// <summary>The running stage has a length: the bar fills (otherwise it moves without a share).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Indeterminate))]
    public partial bool Counted { get; private set; }

    /// <summary>The bar moves without a share.</summary>
    public bool Indeterminate => !Counted;

    /// <summary>The running stage's share, 0–100.</summary>
    [ObservableProperty]
    public partial double Percent { get; private set; }

    /// <summary>"Aşama 2/3 · blok 16/40 · %40 · 3:12" / "geçen süre 0:42".</summary>
    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    /// <summary>
    /// Where the model reports to: on the interface thread at once, from any other thread (the script's output reader) posted there in order.
    /// </summary>
    public IProgress<MusicProgress> Reporter => new Core.Chat.ForwardProgress<MusicProgress>(p =>
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Report(p);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Report(p));
        }
    });

    /// <summary>A piece starts: nothing counted, the time from zero.</summary>
    public void Begin()
    {
        _watch.Restart();
        Active = true;
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
        Report(MusicProgress.Unknown);
    }

    /// <summary>A report of the model (ignored once the piece has ended: a late line of a stopped script).</summary>
    public void Report(MusicProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!Active)
        {
            return;
        }

        _last = progress;
        Counted = progress.Counted;
        Percent = progress.Percent;
        Refresh();
    }

    /// <summary>The piece ended (made, failed or stopped): the bar goes away.</summary>
    public void End()
    {
        _timer?.Stop();
        _watch.Stop();
        Active = false;
        Text = string.Empty;
    }

    private void Refresh()
    {
        if (Active)
        {
            Text = labels.WithTime(_last, _watch.Elapsed);
        }
    }
}

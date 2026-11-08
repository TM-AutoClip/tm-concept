using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Chat;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One model call as a line the user can follow, so a long wait does not look like a hang: "Getting the model ready…" → "Request sent to
/// Claude Haiku 5.5 (2 s)" → "Claude Haiku 5.5 is thinking (14 s)" → "… is writing (~1,200 tokens)" → "Answer received (21 s)". While the
/// model has not written yet the seconds count on by themselves (a thinking model sends nothing for a long time).
/// </summary>
public sealed partial class CallStatusViewModel(Localizer text) : ObservableObject
{
    private readonly Stopwatch _watch = new();
    private DispatcherTimer? _timer;
    private string _model = string.Empty;
    private ChatProgress? _last;
    private bool _preparing;

    /// <summary>The line; empty when no call runs.</summary>
    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    /// <summary>Whether a call is being prepared or runs.</summary>
    [ObservableProperty]
    public partial bool Active { get; private set; }

    /// <summary>The phase of the last report (null before the request is sent).</summary>
    public ChatPhase? Phase => _last?.Phase;

    /// <summary>The model is made ready (key, license, install) before the call.</summary>
    public void Preparing()
    {
        _preparing = true;
        _last = null;
        Active = true;
        Refresh();
    }

    /// <summary>The call with <paramref name="model"/> (its name as the screen shows it) is about to be sent.</summary>
    public void Begin(string model)
    {
        _model = model ?? string.Empty;
        _preparing = false;
        _last = null;
        _watch.Restart();
        Active = true;
        Tick(true);
        Refresh();
    }

    /// <summary>A step of the call (one that arrives after <see cref="Stop"/> is ignored: the call is over).</summary>
    public void Report(ChatProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!Active)
        {
            return;
        }

        if (progress.Phase == ChatPhase.Sent)
        {
            _watch.Restart();
        }

        _preparing = false;
        _last = progress;
        Tick(progress.Phase is ChatPhase.Sent or ChatPhase.Thinking);
        Refresh();
    }

    /// <summary>The call ended (the caller shows the result).</summary>
    public void Stop()
    {
        Tick(false);
        _watch.Reset();
        _preparing = false;
        _last = null;
        Active = false;
        Text = string.Empty;
    }

    private void Tick(bool on)
    {
        if (on)
        {
            _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
            _timer.Start();
        }
        else
        {
            _timer?.Stop();
        }
    }

    private void Refresh()
    {
        if (!Active)
        {
            return;
        }

        var seconds = Seconds(_watch.Elapsed);
        Text = _preparing
            ? text["call.preparing"]
            : _last switch
            {
                null or { Phase: ChatPhase.Sent } => text.Format("call.sent", _model, seconds),
                { Phase: ChatPhase.Thinking } => text.Format("call.thinking", _model, seconds),
                { Phase: ChatPhase.Writing } writing => text.Format("call.writing", _model, writing.ApproxTokens.ToString("N0", text.Culture)),
                { } done => text.Format("call.done", Seconds(done.Elapsed)),
            };
    }

    private static long Seconds(TimeSpan elapsed) => (long)Math.Round(elapsed.TotalSeconds);
}

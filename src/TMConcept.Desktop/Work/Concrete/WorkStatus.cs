using Avalonia.Threading;

namespace TMConcept.Desktop.Work;

/// <summary>
/// What the app is doing right now, for the right side of the strip at the bottom of the main window: every running work (a concept run,
/// "Suggest topics", a video, an install) shows its current step here under its own owner and clears it when it ends; the newest step is
/// shown. Used on the interface thread (a call from another thread is posted there).
/// </summary>
public sealed class WorkStatus
{
    private readonly List<(object Owner, string Text)> _steps = [];

    /// <summary>The shown step changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The newest step; empty when nothing runs.</summary>
    public string Text => _steps.Count > 0 ? _steps[^1].Text : string.Empty;

    /// <summary>Whether a work shows a step.</summary>
    public bool Active => _steps.Count > 0;

    /// <summary>Shows (or replaces) <paramref name="owner"/>'s step.</summary>
    public void Show(object owner, string text)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(owner, text));
            return;
        }

        var at = _steps.FindIndex(s => ReferenceEquals(s.Owner, owner));
        if (at >= 0 && _steps[at].Text == text)
        {
            return;
        }

        if (at >= 0)
        {
            _steps[at] = (owner, text ?? string.Empty);
        }
        else
        {
            _steps.Add((owner, text ?? string.Empty));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes <paramref name="owner"/>'s step (its work ended).</summary>
    public void Clear(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Clear(owner));
            return;
        }

        if (_steps.RemoveAll(s => ReferenceEquals(s.Owner, owner)) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

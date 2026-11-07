using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TMConcept.Core.Components;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// The install window: one line per component of the plan in install order (waiting, running with its progress and what it downloads,
/// done, failed with the reason, skipped because something it needs failed), the whole plan's progress, Cancel while it runs and, after a
/// failure, the reason and a Close button. A finished line never goes back to "running" (progress reports can arrive late).
/// </summary>
public sealed partial class InstallViewModel : ObservableObject, IDisposable
{
    private readonly Localizer _text;
    private readonly CancellationTokenSource _cancel = new();

    /// <summary>Creates the lines of <paramref name="components"/>.</summary>
    public InstallViewModel(IReadOnlyList<IComponent> components, Localizer text)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        Steps = new ObservableCollection<InstallStepViewModel>(components.Select(c => new InstallStepViewModel(c.Code, c.Name) { Detail = text["install.step.waiting"] }));
        Title = text["install.window.title"];
    }

    /// <summary>Title.</summary>
    public string Title { get; }

    /// <summary>The components in install order.</summary>
    public ObservableCollection<InstallStepViewModel> Steps { get; }

    /// <summary>The whole plan's progress 0–100.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>The install is running (Cancel is shown, the window cannot be closed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    public partial bool Running { get; set; } = true;

    /// <summary>Why the install did not finish (empty when it did).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether an error is shown.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>The window may be closed (the install ended).</summary>
    public bool CanClose => !Running;

    /// <summary>Cancel was pressed.</summary>
    public CancellationToken Cancellation => _cancel.Token;

    /// <summary>Raised when the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>A component's state changed.</summary>
    public void Apply(ComponentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Index < 0 || step.Index >= Steps.Count)
        {
            return;
        }

        var line = Steps[step.Index];
        if (line.State is ComponentStepState.Done or ComponentStepState.Failed or ComponentStepState.Skipped && step.State == ComponentStepState.Running)
        {
            return;
        }

        line.State = step.State;
        line.Percent = Math.Round(step.Share * 100);
        line.Detail = step.State switch
        {
            ComponentStepState.Running => step.Detail.Length > 0 ? step.Detail : _text["install.step.running"],
            ComponentStepState.Done => _text["install.step.done"],
            ComponentStepState.Skipped => _text["install.step.skipped"],
            ComponentStepState.Failed => step.Detail,
            _ => _text["install.step.waiting"],
        };
    }

    /// <summary>The install ended: <paramref name="error"/> empty = everything is installed (the window closes by itself).</summary>
    public void Finish(string error)
    {
        Running = false;
        Error = error;
        if (error.Length == 0)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Stops the install after the running download (what finished stays installed).</summary>
    [RelayCommand]
    public void Cancel() => _cancel.Cancel();

    /// <summary>Closes the window after the install ended.</summary>
    [RelayCommand]
    public void Close()
    {
        if (CanClose)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cancel.Dispose();
}

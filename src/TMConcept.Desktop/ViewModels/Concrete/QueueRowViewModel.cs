using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Videos;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A concept waiting to become a video: progress %, the running step, or the error of the last try.</summary>
public sealed partial class QueueRowViewModel(QueueItem item, string title) : ObservableObject
{
    /// <summary>The queue row.</summary>
    public QueueItem Item { get; } = item;

    /// <summary>Title.</summary>
    public string Title { get; } = title;

    /// <summary>Progress 0–100.</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>Running step.</summary>
    [ObservableProperty]
    public partial string Step { get; set; } = string.Empty;

    /// <summary>Running.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Error of the last try.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Failed))]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether the last try failed.</summary>
    public bool Failed => Error.Length > 0;
}

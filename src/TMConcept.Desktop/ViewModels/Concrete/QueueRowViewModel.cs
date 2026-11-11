using CommunityToolkit.Mvvm.ComponentModel;
using TMConcept.Core.Videos;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A concept waiting to become a video: its place in the queue; while it is made, its stage list (<see cref="Progress"/>); after a failed
/// try, the error with what the failed tries paid.
/// </summary>
public sealed partial class QueueRowViewModel(QueueItem item, string title) : ObservableObject
{
    /// <summary>The queue row.</summary>
    public QueueItem Item { get; } = item;

    /// <summary>"#12 Greek fire".</summary>
    public string Title { get; } = title;

    /// <summary>The stages of the running try (null before the first try in this session).</summary>
    [ObservableProperty]
    public partial ProductionProgressViewModel? Progress { get; set; }

    /// <summary>Being made now.</summary>
    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>Error of the last try (with what the failed tries paid).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Failed))]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether the last try failed.</summary>
    public bool Failed => Error.Length > 0;
}

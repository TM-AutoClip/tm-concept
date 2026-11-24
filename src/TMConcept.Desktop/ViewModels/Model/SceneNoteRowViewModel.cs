using CommunityToolkit.Mvvm.ComponentModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>One scene of a plan preview (F10, 0.1.49): its header, its narration and the user's note on it (empty = no note).</summary>
public sealed partial class SceneNoteRowViewModel(int number, string header, string caption) : ObservableObject
{
    /// <summary>Scene number (1-based).</summary>
    public int Number { get; } = number;

    /// <summary>"Scene 2 · map · 0:04".</summary>
    public string Header { get; } = header;

    /// <summary>What the scene says.</summary>
    public string Caption { get; } = caption;

    /// <summary>The user's note on this scene.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;
}

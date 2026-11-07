using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// One scene in the narration editor of a new version: its header and on-screen line (not editable: they belong to the kept code) and its
/// narration text, which can be changed and extended (more sentences, narration markers for pauses and speed).
/// </summary>
public sealed partial class SceneEditViewModel(int number, string header, string visual, string original) : ObservableObject
{
    /// <summary>Scene number (1-based).</summary>
    public int Number { get; } = number;

    /// <summary>"Scene 2 · 4.3 s" (the length of the version it starts from).</summary>
    public string Header { get; } = header;

    /// <summary>What is on screen.</summary>
    public string Visual { get; } = visual;

    /// <summary>Whether there is an on-screen line.</summary>
    public bool HasVisual => Visual.Length > 0;

    /// <summary>The text of the version it starts from.</summary>
    public string Original { get; } = original;

    /// <summary>The narration to speak (markers included).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Changed))]
    public partial string Text { get; set; } = original;

    /// <summary>Whether the text differs from the version it starts from.</summary>
    public bool Changed => !string.Equals(Text, Original, StringComparison.Ordinal);

    /// <summary>Puts the text of the version it starts from back.</summary>
    [RelayCommand]
    public void Reset() => Text = Original;
}

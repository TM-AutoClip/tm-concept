namespace TMConcept.Desktop.ViewModels;

/// <summary>A scene of a made video as the details show it: "Scene 2 · 4.3 s", what is on screen, and the whole narration of the scene.</summary>
public sealed record SceneText(int Number, string Header, string Visual, string Narration)
{
    /// <summary>Whether the scene has a visual line.</summary>
    public bool HasVisual => Visual.Length > 0;
}

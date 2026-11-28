namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A music model on the video page's music choice (0.1.46; 0.1.55 local ones too): its catalog key "provider/id" and its label (price or install
/// state); red when this card's class is lower than it needs, orange when its licence is non-commercial (28 Nov 2026).
/// </summary>
public sealed record MusicModelOption(string Key, string Label, bool IsError = false, bool IsCaution = false);

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A music model on the music page (0.1.54): its key, its label with its price or install state, the model, local or online, installed; red
/// when this card's class is lower than it needs, orange when its licence is non-commercial (28 Nov 2026).
/// </summary>
public sealed record MusicModelChoice(string Key, string Label, Core.Models.CatalogModel Model, bool Local, bool Ready, bool IsError = false, bool IsCaution = false);

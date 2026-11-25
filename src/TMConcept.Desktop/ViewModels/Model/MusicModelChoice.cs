namespace TMConcept.Desktop.ViewModels;

/// <summary>A music model on the music page (0.1.54): its key, its label with its price or install state, the model, local or online, installed.</summary>
public sealed record MusicModelChoice(string Key, string Label, Core.Models.CatalogModel Model, bool Local, bool Ready);

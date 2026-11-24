namespace TMConcept.Desktop.ViewModels;

/// <summary>A music source in the template editor (0.1.45): its number, its label, and whether it can be chosen now (the library only once installed).</summary>
public sealed record MusicSourceChoice(int Value, string Label, bool Enabled);

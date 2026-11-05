namespace TMConcept.Desktop.ViewModels;

/// <summary>A line of a concept's text; a scene line ([on screen: …]) is shown faded and in italics.</summary>
public sealed record ContentLine(string Text, bool IsScene);

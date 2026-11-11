using TMConcept.Core.Concepts;

namespace TMConcept.Desktop.ViewModels;

/// <summary>The detail panel changed a concept's status (approved, taken back, trashed): the page moves its card between its lists.</summary>
public sealed record ConceptStatusChange(ConceptCardViewModel Card, ConceptStatus Status);

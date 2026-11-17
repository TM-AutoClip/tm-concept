namespace TMConcept.Core.Documents;

/// <summary>A document whose passages a prompt holds: its number in the prompt ([1], [2], …), its id and title.</summary>
public sealed record GroundingDocument(int Number, Guid Id, string Title);

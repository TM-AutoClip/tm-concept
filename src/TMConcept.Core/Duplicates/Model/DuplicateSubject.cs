namespace TMConcept.Core.Duplicates;

/// <summary>What the duplicate check compares: number, title and summary of a concept (earlier or new).</summary>
public sealed record DuplicateSubject(Guid Id, int Number, string Title, string Summary);

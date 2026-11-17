namespace TMConcept.Core.Documents;

/// <summary>Adding a document: its name, the stage and, while embedding, passages done of all.</summary>
public sealed record DocumentProgress(string Name, DocumentStage Stage, int Done = 0, int Total = 0);

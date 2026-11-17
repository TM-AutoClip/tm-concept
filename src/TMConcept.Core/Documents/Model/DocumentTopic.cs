namespace TMConcept.Core.Documents;

/// <summary>A topic found in the documents and the titles of the documents that cover it.</summary>
public sealed record DocumentTopic(string Text, IReadOnlyList<string> Documents);

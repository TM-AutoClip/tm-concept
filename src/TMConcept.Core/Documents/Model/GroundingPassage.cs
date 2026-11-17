namespace TMConcept.Core.Documents;

/// <summary>One passage in a prompt: the number of its document, where it is ("p. 12", "Trade") and its text.</summary>
public sealed record GroundingPassage(int Document, string Locator, string Text);

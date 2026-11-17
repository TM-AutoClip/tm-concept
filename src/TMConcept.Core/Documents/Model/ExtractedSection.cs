namespace TMConcept.Core.Documents;

/// <summary>A piece of a document's text as read: the page it is on (0 = none), its heading (empty = none) and the text.</summary>
public sealed record ExtractedSection(int Page, string Heading, string Text);

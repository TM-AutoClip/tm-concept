namespace TMConcept.Core.Documents;

/// <summary>
/// A web page or Wikipedia article as fetched: its title, address, language (an article's; empty otherwise), the content the app keeps as its
/// copy (<see cref="Content"/>: the readable text, or a PDF the address pointed at) and the copy's extension (".md", ".pdf").
/// </summary>
public sealed record FetchedDocument(DocumentKind Kind, string Title, string Url, string Language, byte[] Content, string Extension);

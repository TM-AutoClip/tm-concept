namespace TMConcept.Core.Documents;

/// <summary>
/// A topic found in the documents and the titles of the documents that cover it. <see cref="DocumentIds"/> (those documents) and
/// <see cref="Keywords"/> (a few words in the documents' own language) go with it when a concept is written from it, so its passages are found
/// in those documents even when the topic is written in another language.
/// </summary>
public sealed record DocumentTopic(string Text, IReadOnlyList<string> Documents)
{
    /// <summary>The documents that cover it (empty = not known: every chosen document is searched).</summary>
    public IReadOnlyList<Guid> DocumentIds { get; init; } = [];

    /// <summary>Search words in the documents' language (empty = none).</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

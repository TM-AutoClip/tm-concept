namespace TMConcept.Core.Documents;

/// <summary>
/// A document that cannot be added, with a code the page turns into the user's language: <c>type</c> (not a PDF, Word, text or Markdown
/// file), <c>size</c> (too large), <c>duplicate</c> (the profile has it already; <see cref="Detail"/> is its title), <c>unreadable</c> (damaged
/// or not what its name says), <c>not_found</c> (no such page or article), <c>not_text</c> (the address is not a page, a text or a PDF),
/// <c>network</c> (the address could not be reached; <see cref="Detail"/> holds the system's reason).
/// </summary>
public sealed class DocumentRejectedException : Exception
{
    /// <summary>Creates it.</summary>
    public DocumentRejectedException(string code, string message, string detail = "", Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Detail = detail;
    }

    /// <summary>Creates it without a code (a generic failure).</summary>
    public DocumentRejectedException()
        : this("unreadable", "The document could not be added.")
    {
    }

    /// <summary>Creates it with a message.</summary>
    public DocumentRejectedException(string message)
        : this("unreadable", message)
    {
    }

    /// <summary>Creates it with a message and the cause.</summary>
    public DocumentRejectedException(string message, Exception inner)
        : this("unreadable", message, string.Empty, inner)
    {
    }

    /// <summary>Why (see the class summary).</summary>
    public string Code { get; }

    /// <summary>A detail for the user's message (a title, a reason), or empty.</summary>
    public string Detail { get; }
}

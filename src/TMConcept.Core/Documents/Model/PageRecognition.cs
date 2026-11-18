namespace TMConcept.Core.Documents;

/// <summary>The text a text-extraction model read on a page (empty = no text on it), or that it failed (the reason was logged and counted).</summary>
public sealed record PageRecognition(string Text, bool Failed)
{
    /// <summary>A page whose text could not be read.</summary>
    public static PageRecognition Failure { get; } = new(string.Empty, true);
}

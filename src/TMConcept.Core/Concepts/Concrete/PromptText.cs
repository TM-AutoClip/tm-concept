using TMConcept.Core.Text;

namespace TMConcept.Core.Concepts;

/// <summary>Text that goes into a prompt: cleaned, cut, and with « » taken out so it cannot close a user-data block (rule "« » is user data").</summary>
public static class PromptText
{
    /// <summary>The cleaned text (line breaks kept).</summary>
    public static string Data(string? text, int max) => Guard(TextClean.Clean(text, max));

    /// <summary>The cleaned text on one line.</summary>
    public static string Line(string? text, int max) => Guard(TextClean.OneLine(text, max));

    private static string Guard(string text) => text.Replace("«", "\"", StringComparison.Ordinal).Replace("»", "\"", StringComparison.Ordinal);
}

namespace TMConcept.Core.Text;

/// <summary>Cleans untrusted text from a model: NUL and other control characters removed (line breaks and tabs kept), CRLF → LF, trimmed, cut.</summary>
public static class TextClean
{
    /// <summary>The cleaned text, at most <paramref name="max"/> characters.</summary>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var chars = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Where(c => c == '\n' || c == '\t' || !char.IsControl(c))
            .ToArray();
        var result = new string(chars).Trim();
        return result.Length <= max ? result : result[..max].TrimEnd();
    }

    /// <summary>One line: line breaks become spaces.</summary>
    public static string OneLine(string? text, int max) =>
        Clean(text?.Replace('\n', ' ').Replace('\r', ' '), max);
}

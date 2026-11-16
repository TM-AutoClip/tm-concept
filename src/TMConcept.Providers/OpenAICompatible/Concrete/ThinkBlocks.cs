using System.Text.RegularExpressions;

namespace TMConcept.Providers.OpenAICompatible;

/// <summary>
/// Removes a reasoning model's thinking from an answer's text when a local server leaves it in the content (llama.cpp with
/// <c>--reasoning-format none</c> or <c>deepseek-legacy</c>, some chat templates): every <c>&lt;think&gt;…&lt;/think&gt;</c> block; an unclosed
/// <c>&lt;think&gt;</c> (cut off while thinking) takes the rest; a lone <c>&lt;/think&gt;</c> (the opening tag was part of the prompt template,
/// as in Qwen3) takes everything before it. The answer is read as JSON only after this.
/// </summary>
public static partial class ThinkBlocks
{
    private const string Close = "</think>";

    /// <summary>The text without thinking, trimmed; a text without tags comes back unchanged.</summary>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains("think>", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var result = Block().Replace(text, string.Empty);
        var open = result.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (open >= 0)
        {
            result = result[..open];
        }

        var close = result.LastIndexOf(Close, StringComparison.OrdinalIgnoreCase);
        if (close >= 0)
        {
            result = result[(close + Close.Length)..];
        }

        return result.Trim();
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Block();
}

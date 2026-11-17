using TMConcept.Core.Scenes;

namespace TMConcept.Core.Styles;

/// <summary>
/// The spoken closing: the template's closing sentence (in the video's language, fixed text, no model) is added to the end of the last scene's
/// narration before the voice reads it, so the voice says it, the timing follows the real clip and the subtitles show it. It becomes part of
/// the video's kept narration, so a new version reads it again as it is (it is never added twice).
/// </summary>
public static class ClosingNarration
{
    /// <summary>
    /// <paramref name="scenes"/> with <paramref name="sentence"/> after the last scene's narration and script; the same list when the sentence
    /// is empty, there is no scene, or the narration already ends with it.
    /// </summary>
    public static IReadOnlyList<Scene> Append(IReadOnlyList<Scene> scenes, string sentence)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var text = sentence?.Trim() ?? string.Empty;
        if (text.Length == 0 || scenes.Count == 0)
        {
            return scenes;
        }

        var last = scenes[^1];
        if (last.Narration.TrimEnd().EndsWith(text, StringComparison.Ordinal))
        {
            return scenes;
        }

        var closed = last with
        {
            Narration = Join(last.Narration, text),
            Script = Join(last.Script, text),
        };
        return [.. scenes.Take(scenes.Count - 1), closed];
    }

    private static string Join(string text, string sentence)
    {
        var body = text.TrimEnd();
        if (body.Length == 0)
        {
            return sentence;
        }

        // A narration that ends without a full stop gets one, so the voice and the subtitles end the sentence before the closing (a script
        // that ends with a placeholder, a pause, gets none).
        return SubtitleEndsSentence(body) || body.EndsWith(']') ? body + " " + sentence : body + ". " + sentence;
    }

    private static bool SubtitleEndsSentence(string text) => Subtitles.SubtitleBuilder.EndsSentence(text.Split(' ')[^1]);
}

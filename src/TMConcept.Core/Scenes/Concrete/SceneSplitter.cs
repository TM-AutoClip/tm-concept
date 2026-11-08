using System.Text;
using System.Text.RegularExpressions;
using TMConcept.Core.Markers;

namespace TMConcept.Core.Scenes;

/// <summary>
/// Splits a concept's script into scenes locally (no model call). A line in square brackets — <c>[on screen: …]</c>,
/// <c>[ekranda: …]</c> or just <c>[…]</c> — starts a new scene and is its visual; the other lines until the next bracket line are its
/// narration. Text before the first bracket line becomes a scene with an empty visual. Bracket text inside a sentence is removed from the
/// narration (the model is told not to use brackets elsewhere). The placeholders of <see cref="MarkerCatalog"/> are not scenes: a pause or
/// speed placeholder stays in the scene's <see cref="Scene.Script"/> where it stands (on a line of its own too), and a transition
/// placeholder (<c>[sert geçiş]</c>, <c>[yumuşak geçiş]</c>) sets how a scene begins: the scene whose text it is in (its narration or its
/// scene line), or, on a line of its own, the next scene (one on a line of its own after the last scene begins nothing and is ignored; it is
/// not a problem, so the text can still be saved). The first scene has no transition. The script (with its placeholders) is cut where its
/// spoken text reaches <see cref="MaxNarration"/>, like the narration, so voice and subtitles say the same.
/// </summary>
public static partial class SceneSplitter
{
    /// <summary>Most scenes in one video.</summary>
    public const int MaxScenes = 40;

    /// <summary>Longest narration of one scene.</summary>
    public const int MaxNarration = 2000;

    /// <summary>Longest visual description.</summary>
    public const int MaxVisual = 600;

    /// <summary>Whether a line is only a scene description ("[on screen: …]" / "[ekranda: …]"), not only placeholders.</summary>
    public static bool IsSceneLine(string? line)
    {
        var text = (line ?? string.Empty).Trim();
        return text.Length > 0 && Bracket().IsMatch(text) && Bracket().Replace(text, string.Empty).Trim().Length == 0
            && Bracket().Matches(text).Any(m => MarkerText.Find(m.Value) is null);
    }

    /// <summary>Splits.</summary>
    public static SceneSplit Split(string? content)
    {
        var scenes = new List<Draft>();
        var transition = SceneTransitions.None;
        foreach (var raw in (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var brackets = Bracket().Matches(line);
            var visual = brackets.FirstOrDefault(m => MarkerText.Find(m.Value) is null);
            var bracketsOnly = brackets.Count > 0 && Bracket().Replace(line, string.Empty).Trim().Length == 0;
            var asked = MarkerText.Transition(line);
            if (bracketsOnly && visual is not null)
            {
                // A scene line: a transition on it, or on a line of its own before it, is how it begins.
                scenes.Add(new Draft(Cut(StripPrefix(visual.Groups[1].Value), MaxVisual)) { Transition = asked.Length > 0 ? asked : transition });
                transition = SceneTransitions.None;
                continue;
            }

            if (bracketsOnly && asked.Length > 0 && !brackets.Any(m => MarkerText.Find(m.Value) is { Kind: MarkerKind.Narration }))
            {
                // Only transitions on a line of their own: they stand before the next scene.
                transition = asked;
                continue;
            }

            if (scenes.Count == 0)
            {
                scenes.Add(new Draft(string.Empty));
            }

            // Narration: without any bracket text; the script keeps the narration placeholders where they stand; a transition in it is how
            // this scene begins.
            var draft = scenes[^1];
            if (asked.Length > 0)
            {
                draft.Transition = asked;
            }

            Append(draft.Narration, Bracket().Replace(line, string.Empty));
            Append(draft.Script, Bracket().Replace(line, m => MarkerText.Find(m.Value) is { Kind: MarkerKind.Narration } ? m.Value : " "));
        }

        var problems = new List<string>();
        var result = scenes
            .Where(s => s.Visual.Length > 0 || s.Narration.Length > 0)
            .Take(MaxScenes)
            .Select((s, i) => new Scene(i + 1, s.Visual, Cut(s.Narration.ToString(), MaxNarration))
            {
                Script = CutSpoken(s.Script.ToString(), MaxNarration),
                Transition = i == 0 ? SceneTransitions.None : s.Transition,
            })
            .ToList();
        if (result.Count == 0)
        {
            problems.Add("The script is empty.");
        }
        else if (result.All(s => s.Narration.Length == 0))
        {
            problems.Add("The script has no narration.");
        }

        if (scenes.Count > MaxScenes)
        {
            problems.Add($"The script has {scenes.Count} scenes; at most {MaxScenes} are used.");
        }

        return new SceneSplit(result, problems);
    }

    private static void Append(StringBuilder to, string text)
    {
        var value = Spaces().Replace(text, " ").Trim();
        if (value.Length == 0)
        {
            return;
        }

        if (to.Length > 0)
        {
            to.Append(' ');
        }

        to.Append(value);
    }

    private static string StripPrefix(string visual)
    {
        var text = visual.Trim();
        var match = Prefix().Match(text);
        return (match.Success ? text[match.Length..] : text).Trim();
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();

    /// <summary>
    /// A script cut where its spoken text (without the placeholders, spaces collapsed) reaches <paramref name="max"/> characters, so the voice
    /// never reads past what the narration and the subtitles keep (0.1.7 allowed 400 characters more for the placeholders, and with no
    /// placeholder the voice read up to 400 characters the subtitles did not show).
    /// </summary>
    private static string CutSpoken(string script, int max)
    {
        var spoken = 0;
        var inBracket = false;
        var lastSpace = true;
        for (var i = 0; i < script.Length; i++)
        {
            var ch = script[i];
            if (ch == '[')
            {
                inBracket = true;
                continue;
            }

            if (inBracket)
            {
                inBracket = ch != ']';
                continue;
            }

            var space = char.IsWhiteSpace(ch);
            if (space && lastSpace)
            {
                continue;
            }

            lastSpace = space;
            if (++spoken > max)
            {
                return script[..i].TrimEnd();
            }
        }

        return script;
    }

    [GeneratedRegex(@"\[([^\[\]]*)\]")]
    private static partial Regex Bracket();

    [GeneratedRegex(@"^(?:on\s*screen|ekranda|screen|ekran|visual|görsel)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Prefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>A scene while the lines are read.</summary>
    private sealed class Draft(string visual)
    {
        public string Visual { get; } = visual;

        public string Transition { get; set; } = SceneTransitions.None;

        public StringBuilder Narration { get; } = new();

        public StringBuilder Script { get; } = new();
    }
}

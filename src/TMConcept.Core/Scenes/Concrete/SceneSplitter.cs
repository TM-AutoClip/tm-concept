using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Scenes;

/// <summary>
/// Splits a concept's script into scenes locally (no model call). A line in square brackets — <c>[on screen: …]</c>,
/// <c>[ekranda: …]</c> or just <c>[…]</c> — starts a new scene and is its visual; the other lines until the next bracket line are its
/// narration. Text before the first bracket line becomes a scene with an empty visual. Bracket text inside a sentence is treated as a
/// visual too and removed from the narration (the model is told not to use brackets elsewhere).
/// </summary>
public static partial class SceneSplitter
{
    /// <summary>Most scenes in one video.</summary>
    public const int MaxScenes = 40;

    /// <summary>Longest narration of one scene.</summary>
    public const int MaxNarration = 2000;

    /// <summary>Longest visual description.</summary>
    public const int MaxVisual = 600;

    /// <summary>Whether a line is only a scene description ("[on screen: …]" / "[ekranda: …]").</summary>
    public static bool IsSceneLine(string? line)
    {
        var text = (line ?? string.Empty).Trim();
        return text.Length > 0 && Bracket().IsMatch(text) && Bracket().Replace(text, string.Empty).Trim().Length == 0;
    }

    /// <summary>Splits.</summary>
    public static SceneSplit Split(string? content)
    {
        var scenes = new List<(string Visual, StringBuilder Narration)>();
        foreach (var raw in (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var visuals = Bracket().Matches(line);
            if (visuals.Count > 0 && Bracket().Replace(line, string.Empty).Trim().Length == 0)
            {
                scenes.Add((Cut(StripPrefix(visuals[0].Groups[1].Value), MaxVisual), new StringBuilder()));
                continue;
            }

            if (scenes.Count == 0)
            {
                scenes.Add((string.Empty, new StringBuilder()));
            }

            var text = Bracket().Replace(line, string.Empty).Trim();
            if (text.Length > 0)
            {
                var narration = scenes[^1].Narration;
                if (narration.Length > 0)
                {
                    narration.Append(' ');
                }

                narration.Append(Spaces().Replace(text, " "));
            }
        }

        var problems = new List<string>();
        var result = scenes
            .Where(s => s.Visual.Length > 0 || s.Narration.Length > 0)
            .Take(MaxScenes)
            .Select((s, i) => new Scene(i + 1, s.Visual, Cut(s.Narration.ToString(), MaxNarration)))
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

    private static string StripPrefix(string visual)
    {
        var text = visual.Trim();
        var match = Prefix().Match(text);
        return (match.Success ? text[match.Length..] : text).Trim();
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();

    [GeneratedRegex(@"\[([^\[\]]*)\]")]
    private static partial Regex Bracket();

    [GeneratedRegex(@"^(?:on\s*screen|ekranda|screen|ekran|visual|görsel)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Prefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

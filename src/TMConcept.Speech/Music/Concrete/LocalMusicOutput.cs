using TMConcept.Core.Processes;

namespace TMConcept.Speech.Music;

/// <summary>
/// Why a local music script failed, from its last lines (28 Nov 2026): its own <c>TMC-E-…</c> line when it wrote one (an exception it caught:
/// the card out of memory, a missing file, no CUDA), otherwise the general summary of a child program's output (a crash before the script
/// could say anything, such as a package that would not import).
/// </summary>
public static class LocalMusicOutput
{
    /// <summary>The reason of a failed run, at most <see cref="LocalMusicRunRules.ReasonCharacters"/> characters.</summary>
    public static string Reason(string? lines)
    {
        var chosen = Lines(lines).Where(l => l.StartsWith(LocalMusicRunRules.ErrorMark, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
        return chosen.Count == 0
            ? ChildOutput.Summary(lines)
            : ChildOutput.Summary(string.Join(" | ", chosen), lines: 1, characters: LocalMusicRunRules.ReasonCharacters);
    }

    /// <summary>Whether the script said the graphics card ran out of memory.</summary>
    public static bool OutOfMemory(string? lines) => Lines(lines).Any(l => l.StartsWith(LocalMusicRunRules.MemoryMark, StringComparison.Ordinal));

    private static IEnumerable<string> Lines(string? lines) =>
        (lines ?? string.Empty).Split('\n').Select(l => l.Trim().TrimEnd('\r')).Where(l => l.Length > 0);
}

using System.Globalization;
using TMConcept.Core.Music;

namespace TMConcept.Speech.Music;

/// <inheritdoc cref="IMusicProgressParser" />
/// <remarks>
/// The line is <c>TMC-P: &lt;stage&gt;/&lt;stages&gt; &lt;done&gt;/&lt;total&gt; &lt;unit&gt;</c> (<see cref="LocalMusicRunRules.ProgressMark"/>),
/// with the unit one of the words of <see cref="Units"/>. Anything else (a stage after the last, more done than the total, an unknown unit) is
/// not a progress line: a script never prints one, so it is a line of the model's own output that happens to start the same way.
/// </remarks>
public sealed class MusicProgressLineParser : IMusicProgressParser
{
    /// <summary>The unit words of the scripts (<c>tmc_progress.py</c>).</summary>
    public static IReadOnlyDictionary<string, MusicProgressUnit> Units { get; } = new Dictionary<string, MusicProgressUnit>(StringComparer.Ordinal)
    {
        ["tokens"] = MusicProgressUnit.Tokens,
        ["blocks"] = MusicProgressUnit.Blocks,
        ["steps"] = MusicProgressUnit.Steps,
        ["decoding"] = MusicProgressUnit.Decoding,
    };

    /// <inheritdoc />
    public MusicProgress? Parse(string line)
    {
        if (line is null || !line.StartsWith(LocalMusicRunRules.ProgressMark, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = line[LocalMusicRunRules.ProgressMark.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !Pair(parts[0], out var stage, out var stages) || !Pair(parts[1], out var done, out var total)
            || !Units.TryGetValue(parts[2], out var unit))
        {
            return null;
        }

        var valid = stage >= 1 && stage <= stages && stages <= int.MaxValue && done >= 0 && total >= 0 && (total == 0 ? done == 0 : done <= total);
        return valid ? new MusicProgress((int)stage, (int)stages, done, total, unit) : null;
    }

    private static bool Pair(string text, out long first, out long second)
    {
        first = second = 0;
        var at = text.IndexOf('/', StringComparison.Ordinal);
        return at > 0 && long.TryParse(text.AsSpan(0, at), NumberStyles.None, CultureInfo.InvariantCulture, out first)
            && long.TryParse(text.AsSpan(at + 1), NumberStyles.None, CultureInfo.InvariantCulture, out second);
    }
}

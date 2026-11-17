using System.Text;

namespace TMConcept.Documents.Web;

/// <summary>
/// The rules of a site's robots.txt for the app (RFC 9309), honoured when the app fetches a page it found itself (a web search's or an MCP
/// server's hit, not an address the user typed): the group of the app's product token ("tm-concept") when there is one, else the group of
/// "*"; of the Allow and Disallow rules that match a path the longest wins, Allow on a tie; "*" matches any characters and "$" ends the path.
/// No group or no matching rule allows the path.
/// </summary>
public sealed class RobotsTxt
{
    /// <summary>The product token the app looks for in "User-agent" lines.</summary>
    public const string ProductToken = "tm-concept";

    /// <summary>Rules allowing everything (no robots.txt, or one the site does not have).</summary>
    public static readonly RobotsTxt AllowAll = new([]);

    /// <summary>Rules refusing everything (the site's robots.txt could not be reached: RFC 9309 says to assume a full disallow).</summary>
    public static readonly RobotsTxt DisallowAll = new([(false, "/")]);

    private readonly IReadOnlyList<(bool Allow, string Pattern)> _rules;

    private RobotsTxt(IReadOnlyList<(bool Allow, string Pattern)> rules) => _rules = rules;

    /// <summary>The rules of <paramref name="text"/> that apply to the app.</summary>
    public static RobotsTxt Parse(string? text)
    {
        var groups = new List<(List<string> Agents, List<(bool Allow, string Pattern)> Rules)>();
        (List<string> Agents, List<(bool Allow, string Pattern)> Rules)? current = null;
        var lastWasAgent = false;
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = (raw.IndexOf('#', StringComparison.Ordinal) is var hash and >= 0 ? raw[..hash] : raw).Trim();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (field == "user-agent")
            {
                if (!lastWasAgent || current is null)
                {
                    current = ([], []);
                    groups.Add(current.Value);
                }

                current.Value.Agents.Add(value.ToLowerInvariant());
                lastWasAgent = true;
                continue;
            }

            lastWasAgent = false;
            if (current is { } group && field is "allow" or "disallow" && value.Length > 0)
            {
                group.Rules.Add((field == "allow", value));
            }
        }

        var mine = groups.Where(g => g.Agents.Any(a => a == ProductToken)).SelectMany(g => g.Rules).ToList();
        var rules = mine.Count > 0 || groups.Any(g => g.Agents.Any(a => a == ProductToken))
            ? mine
            : groups.Where(g => g.Agents.Contains("*")).SelectMany(g => g.Rules).ToList();
        return new RobotsTxt(rules);
    }

    /// <summary>Whether the app may fetch <paramref name="address"/>.</summary>
    public bool Allows(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var path = address.PathAndQuery.Length > 0 ? address.PathAndQuery : "/";
        var best = -1;
        var allowed = true;
        foreach (var (allow, pattern) in _rules)
        {
            if (!Matches(path, pattern))
            {
                continue;
            }

            if (pattern.Length > best || (pattern.Length == best && allow))
            {
                best = pattern.Length;
                allowed = allow;
            }
        }

        return allowed;
    }

    /// <summary>
    /// Whether <paramref name="path"/> starts with <paramref name="pattern"/> ("*" any characters, a final "$" the end). Matched without
    /// backtracking into every star (time grows with the path times the pattern), so a hostile robots.txt cannot stall the app.
    /// </summary>
    private static bool Matches(string path, string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var glob = Normalize(anchored ? pattern[..^1] : pattern) + (anchored ? string.Empty : "*");
        var text = Normalize(path);
        int p = 0, s = 0, star = -1, mark = 0;
        while (s < text.Length)
        {
            if (p < glob.Length && glob[p] != '*' && glob[p] == text[s])
            {
                p++;
                s++;
            }
            else if (p < glob.Length && glob[p] == '*')
            {
                star = p++;
                mark = s;
            }
            else if (star >= 0)
            {
                p = star + 1;
                s = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < glob.Length && glob[p] == '*')
        {
            p++;
        }

        return p == glob.Length;
    }

    /// <summary>Percent-escapes compared in one form (upper-case hex; unreserved characters unescaped would need a full decoder, so only the case is unified).</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 2 < text.Length)
            {
                builder.Append('%').Append(char.ToUpperInvariant(text[i + 1])).Append(char.ToUpperInvariant(text[i + 2]));
                i += 2;
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }
}

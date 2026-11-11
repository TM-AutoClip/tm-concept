using System.Text.RegularExpressions;

namespace TMConcept.Core.Engines;

/// <summary>
/// Static check of the code a model wrote. The code is untrusted input: it is checked before it is written to disk and before it is
/// rendered; any finding means the code is not written and not run (a rejected code is kept as a text file for the user to read).
/// Rule: the code only draws. Its data comes from the render input; no network, files, processes, storage, dynamic code, other windows.
/// <list type="bullet">
/// <item>Imports are allow-listed: the engine's packages and the skeleton's <c>./input</c> file. HyperFrames code has no imports at all.</item>
/// <item>Forbidden patterns: fetch / XMLHttpRequest / WebSocket / EventSource / sendBeacon, dynamic import, eval / Function, require /
/// process / quoted node: modules, storage and cookies, window.open / the page's location and frames, workers and messaging, script / iframe /
/// link tags, http(s) / ws addresses, escapes of ASCII characters and other ways to hide text (fromCharCode, atob), global access by
/// brackets, constructor / __proto__ chains, HTML injection. Ordinary names (a parameter "node: Rect", a variable "location" or "parent")
/// and escapes of non-ASCII characters pass: a false rejection costs a paid repair.</item>
/// </list>
/// This is not the only defense: the HyperFrames page has a content security policy without network, the Remotion bundler resolves only
/// inside the work folder and the engine packages, Revideo's browser gets a dead proxy, and engine processes start without secrets in their
/// environment.
/// </summary>
public static partial class EngineCodeCheck
{
    /// <summary>Longest accepted code.</summary>
    public const int MaxCharacters = 150_000;

    /// <summary>The SVG namespace (allowed: needed by createElementNS).</summary>
    public const string SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>The XLink namespace (allowed).</summary>
    public const string XlinkNamespace = "http://www.w3.org/1999/xlink";

    private static readonly (Regex Pattern, string Reason)[] Forbidden =
    [
        (new(@"\bfetch\b", RegexOptions.IgnoreCase), "network request (fetch)"),
        (new(@"XMLHttpRequest|WebSocket|EventSource|sendBeacon|RTCPeerConnection", RegexOptions.IgnoreCase), "network connection"),
        (new(@"\bimport\s*\(|\bimport\s*\.\s*meta\b"), "dynamic import (import() / import.meta)"),
        (new(@"\beval\s*\(|\bnew\s+Function\b|\bFunction\s*\(|setTimeout\s*\(\s*['""`]|setInterval\s*\(\s*['""`]"), "dynamic code"),
        // "node:" only as a quoted module name: a TypeScript annotation such as (node: Rect) is ordinary drawing code.
        (new(@"\brequire\s*\(|\bprocess\s*\.|child_process|['""`]node:|__dirname|__filename"), "Node.js access"),
        (new(@"document\s*\.\s*cookie|localStorage|sessionStorage|indexedDB|caches\s*\.|navigator\s*\.", RegexOptions.IgnoreCase), "browser storage or device access"),
        // The page's own location and frames; a variable called location / top / parent (a map pin, a layout box) is not one of them.
        (new(@"window\s*\.\s*open|\b(window|document|self|globalThis|top|parent|frames)\s*\.\s*location\b|\blocation\s*\.\s*(href|assign|replace|reload)\b|(?<!\b(?:const|let|var)\s+)(?<![\.\w$])location\s*=(?!=)|document\s*\.\s*domain|\bopener\b|\b(window|self|globalThis)\s*\.\s*(top|parent|frames)\b|\b(top|parent)\s*\.\s*(document|postMessage|frames|window)\b"), "another page or window"),
        (new(@"\bWorker\s*\(|SharedWorker|importScripts|postMessage|BroadcastChannel|serviceWorker"), "workers or messaging"),
        (new(@"<\s*(script|iframe|object|embed|link|base|meta|form)\b", RegexOptions.IgnoreCase), "embedded tag (script / iframe / link …)"),
        (new(@"@import|url\s*\(\s*['""]?\s*(https?:|//|data:text)", RegexOptions.IgnoreCase), "external resource (CSS)"),
        (new(@"\b(https?|wss?|ftp|file):", RegexOptions.IgnoreCase), "address (http / ws / file)"),
        // Escapes are checked separately (AsciiEscape): only an escape for an ASCII character can hide a name such as "fetch".
        (new(@"fromCharCode|fromCodePoint|\batob\s*\(|\bbtoa\s*\(|unescape\s*\(|decodeURI"), "hidden text (escape sequences)"),
        (new(@"\b(window|globalThis|self|document)\s*\["), "global access by brackets"),
        (new(@"constructor\s*[\.\[]|\[\s*['""`]constructor|__proto__|prototype\s*\[|Reflect\s*\.|\bProxy\s*\(|defineProperty"), "object chain escape"),
        // Comments are not skipped, on purpose: telling a comment from code needs a real parser ("//" also occurs inside strings and
        // regex literals), and a wrong guess would let code through. A harmless "// no innerHTML" is refused; the prompt says so instead.
        (new(@"innerHTML|outerHTML|insertAdjacentHTML|document\s*\.\s*write|createContextualFragment|DOMParser|srcdoc", RegexOptions.IgnoreCase), "HTML injection (use textContent)"),
    ];

    /// <summary>Allowed imports of an engine (exact name, or a prefix ending with '/').</summary>
    public static IReadOnlyList<string> AllowedImports(EngineKind engine) => engine switch
    {
        EngineKind.Remotion => ["remotion", "react", "./input"],
        EngineKind.Revideo => ["@revideo/2d", "@revideo/core", "./input"],
        _ => [],
    };

    /// <summary>The structure the skeleton calls (the code must contain it).</summary>
    public static (Regex Pattern, string Description) RequiredExport(EngineKind engine) => engine switch
    {
        EngineKind.Remotion => (RemotionScene(), "export const Scene (or export function Scene)"),
        EngineKind.Revideo => (RevideoScene(), "export default makeScene2D('scene', function* (view) { … })"),
        EngineKind.HyperFrames => (HyperFramesTimeline(), "window.__timelines[\"concept\"] = <GSAP timeline>"),
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };

    /// <summary>Findings (empty = passed). Each finding: line number, reason and the start of the line.</summary>
    public static IReadOnlyList<string> Check(EngineKind engine, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ["The code is empty."];
        }

        if (code.Length > MaxCharacters)
        {
            return [$"The code is too long ({code.Length} characters; at most {MaxCharacters})."];
        }

        if (code.Contains('\0', StringComparison.Ordinal))
        {
            return ["The code contains a NUL character."];
        }

        var findings = new List<string>();
        var lines = code.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Replace(SvgNamespace, string.Empty, StringComparison.Ordinal).Replace(XlinkNamespace, string.Empty, StringComparison.Ordinal);
            foreach (var (pattern, reason) in Forbidden)
            {
                if (pattern.IsMatch(line))
                {
                    findings.Add($"line {i + 1}: {reason} — {Head(lines[i])}");
                }
            }

            if (HidesAscii(line))
            {
                findings.Add($"line {i + 1}: hidden text (an escape sequence for an ASCII character) — {Head(lines[i])}");
            }
        }

        var allowed = AllowedImports(engine);
        foreach (Match match in Import().Matches(code))
        {
            var module = match.Groups[2].Value;
            var ok = allowed.Any(p => module == p || (p.EndsWith('/') && module.StartsWith(p, StringComparison.Ordinal)));
            if (!ok)
            {
                findings.Add(allowed.Count == 0
                    ? $"Import not allowed ('{module}'): this engine's code is a plain script."
                    : $"Import not allowed: '{module}' (allowed: {string.Join(", ", allowed)}).");
            }
        }

        if (allowed.Count == 0 && ModuleSyntax().IsMatch(code) && !findings.Any(f => f.StartsWith("Import not allowed (", StringComparison.Ordinal)))
        {
            findings.Add("Module syntax (import / export) is not allowed: this engine's code is a plain script.");
        }

        var (required, description) = RequiredExport(engine);
        if (!required.IsMatch(code))
        {
            findings.Add($"Required structure missing: {description}.");
        }

        return findings;
    }

    /// <summary>The code from a model answer: the first code fence (```tsx / ```js …) when there is one, otherwise the whole text.</summary>
    public static string ExtractCode(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return string.Empty;
        }

        var match = CodeFence().Match(answer);
        return (match.Success ? match.Groups[1].Value : answer).Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";
    }

    /// <summary>
    /// Whether a line has a \x, \u or \u{…} escape of an ASCII character (below 0x80): that is how a forbidden name can be spelled without its
    /// letters. Escapes of other characters ("\u2014" for —, "\u00b7" for ·) are ordinary text and pass.
    /// </summary>
    private static bool HidesAscii(string line)
    {
        foreach (Match match in AsciiEscape().Matches(line))
        {
            var hex = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            if (hex.Length > 6 || int.Parse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture) < 0x80)
            {
                return true;
            }
        }

        return false;
    }

    private static string Head(string line)
    {
        var text = line.Trim();
        return text.Length <= 80 ? text : text[..80] + "…";
    }

    /// <summary>
    /// Module specifier of <c>from '…'</c> and a bare <c>import '…'</c> anywhere on a line, with comments or spaces in between and quoted
    /// names, so a bundler cannot be made to embed a file outside the work folder. Text inside strings or comments is caught too (on purpose).
    /// </summary>
    [GeneratedRegex(@"\b(?:from|import)\b(?:\s|/\*[\s\S]*?\*/|//[^\n]*(?:\n|$))*(['""`])(.*?)\1")]
    private static partial Regex Import();

    [GeneratedRegex(@"\\x([0-9a-fA-F]{2})|\\u\{([0-9a-fA-F]+)\}|\\u([0-9a-fA-F]{4})")]
    private static partial Regex AsciiEscape();

    [GeneratedRegex(@"\bexport\s+(?:const|function)\s+Scene\b")]
    private static partial Regex RemotionScene();

    [GeneratedRegex(@"window\.__timelines\s*\[\s*[""']concept[""']\s*\]\s*=")]
    private static partial Regex HyperFramesTimeline();

    [GeneratedRegex(@"\bexport\s+default\s+makeScene2D\s*\(")]
    private static partial Regex RevideoScene();

    [GeneratedRegex(@"^\s*(?:import|export)\b", RegexOptions.Multiline)]
    private static partial Regex ModuleSyntax();

    [GeneratedRegex(@"```[a-zA-Z0-9_+-]*[ \t]*\r?\n(.*?)\r?\n?```", RegexOptions.Singleline)]
    private static partial Regex CodeFence();
}

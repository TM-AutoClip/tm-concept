using System.Text.RegularExpressions;

namespace TMConcept.Tests.Architecture;

/// <summary>
/// FOLDER LAYOUT (rule FOLDER1). Every source file's single top-level type lives in the category folder of its feature folder:
/// <c>Abstract/</c> interface, delegate · <c>Base/</c> abstract class / record · <c>Constant/</c> enum, static class with only
/// <c>const</c> / <c>static readonly</c> members · <c>Model/</c> record, struct, class with only auto-properties · <c>Concrete/</c> every other
/// class (services, helpers, view models, exceptions). A category folder may have sub folders. The namespace does not follow the category
/// folder. Kept in place: <c>Program.cs</c>, views and their code-behind (<c>.axaml.cs</c>), generated code, tests.
/// </summary>
internal static partial class FolderLayoutRules
{
    /// <summary>Rule id.</summary>
    public const string Rule = "FOLDER1";

    /// <summary>Category folder names.</summary>
    public static readonly IReadOnlyList<string> Categories = ["Abstract", "Base", "Concrete", "Constant", "Model"];

    private static readonly string[] KeptNames = ["Program.cs", "GlobalUsings.cs", "AssemblyInfo.cs"];

    private static readonly string[] ViewExtensions = [".axaml.cs", ".axaml"];

    /// <summary>Violations of the given files; <paramref name="exists"/> answers whether a repository-relative file exists.</summary>
    public static IReadOnlyList<string> Analyze(IReadOnlyCollection<(string Path, string Text)> files, Func<string, bool> exists)
    {
        var byPath = files.ToDictionary(f => f.Path, f => f.Text, StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var (path, text) in files.Where(f => f.Path.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            if (KeptNames.Contains(name, StringComparer.Ordinal) || ViewExtensions.Any(e => name.EndsWith(e, StringComparison.Ordinal)))
            {
                continue;
            }

            var folder = path[..path.LastIndexOf('/')];
            var baseName = name[..name.IndexOf('.', StringComparison.Ordinal)];
            if (ViewExtensions.Any(e => exists($"{folder}/{baseName}{e}")))
            {
                continue;
            }

            // A part file takes the category of its main file.
            var main = $"{folder}/{baseName}.cs";
            var source = name != baseName + ".cs" && byPath.TryGetValue(main, out var mainText) ? mainText : text;
            if (Kind(source) is not { } expected)
            {
                continue;
            }

            var actual = folder.Split('/').LastOrDefault(s => Categories.Contains(s, StringComparer.Ordinal));
            if (actual != expected)
            {
                violations.Add($"{Rule} {path}: '{baseName}' belongs in {expected}/ (now {(actual is null ? "no category folder" : actual + "/")})");
            }
        }

        return violations;
    }

    /// <summary>Category of the file's single top-level type; <c>null</c> when no type is found.</summary>
    public static string? Kind(string text)
    {
        var clean = LineComment().Replace(BlockComment().Replace(text, string.Empty), string.Empty);
        var declaration = Declaration().Match(clean);
        if (!declaration.Success)
        {
            return null;
        }

        var modifiers = declaration.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kind = Whitespace().Replace(declaration.Groups[2].Value, " ");
        var body = clean[(declaration.Index + declaration.Length)..];
        return kind switch
        {
            "interface" or "delegate" => "Abstract",
            "enum" => "Constant",
            _ when modifiers.Contains("abstract") => "Base",
            "class" when modifiers.Contains("static") => OnlyConstants(body) ? "Constant" : "Concrete",
            "record" or "record class" or "record struct" or "struct" => "Model",
            "class" when IsDataClass(body) => "Model",
            _ => "Concrete",
        };
    }

    private static bool OnlyConstants(string body)
    {
        var members = StaticMember().Matches(body).Select(m => m.Value).ToList();
        return members.Count > 0 && members.All(m => ConstantMember().IsMatch(m));
    }

    private static bool IsDataClass(string body)
    {
        var lines = MemberLine().Matches(body).Select(m => m.Groups[1].Value.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('[') && l is not "{" and not "}").ToList();
        return lines.Count > 0 && lines.All(l =>
            (AutoProperty().IsMatch(l) && !l.Split('{')[0].Contains("=>", StringComparison.Ordinal)) || ConstMember().IsMatch(l));
    }

    [GeneratedRegex(@"^\s*((?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|ref|unsafe|file|new)\s+)*)(interface|enum|delegate|record\s+struct|record\s+class|record|class|struct)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^    (?:public|internal|private|protected)\s[^\n]*", RegexOptions.Multiline)]
    private static partial Regex StaticMember();

    [GeneratedRegex(@"\bconst\b|static readonly")]
    private static partial Regex ConstantMember();

    [GeneratedRegex(@"^    (?!//)(\S[^\n]*)$", RegexOptions.Multiline)]
    private static partial Regex MemberLine();

    [GeneratedRegex(@"\{\s*(get|init|set)\s*;")]
    private static partial Regex AutoProperty();

    [GeneratedRegex(@"\bconst\b")]
    private static partial Regex ConstMember();
}

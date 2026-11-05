using System.Text.RegularExpressions;
using Shouldly;
using TMConcept.Desktop.Localization;
using TMConcept.Tests.Architecture;

namespace TMConcept.Tests.Desktop;

/// <summary>Both language files have exactly the same keys, and every key used in views and code exists.</summary>
public sealed partial class LocalizationTests
{
    [Fact]
    public void EnglishAndTurkishHaveTheSameKeys()
    {
        Localizer.Supported.ShouldBe(["en", "tr"]);
        var en = Localizer.Keys("en").ToHashSet(StringComparer.Ordinal);
        var tr = Localizer.Keys("tr").ToHashSet(StringComparer.Ordinal);
        en.Except(tr).ShouldBeEmpty("missing in tr.json");
        tr.Except(en).ShouldBeEmpty("missing in en.json");
    }

    [Fact]
    public void KeysUsedInViewsExist()
    {
        var en = Localizer.Keys("en").ToHashSet(StringComparer.Ordinal);
        var used = Directory.EnumerateFiles(Path.Combine(RepositoryFiles.Root, "src", "TMConcept.Desktop"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => ResourceKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(k => !k.StartsWith("tm-", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);
        used.Where(k => !en.Contains(k)).ShouldBeEmpty();
    }

    [Fact]
    public void KeysWrittenInCodeExist()
    {
        var en = Localizer.Keys("en").ToHashSet(StringComparer.Ordinal);
        var prefixes = string.Join('|', en.Select(k => k.Split('.')[0]).Distinct(StringComparer.Ordinal).Select(Regex.Escape));
        var pattern = new Regex("\"((?:" + prefixes + ")\\.[a-z0-9_.]*[a-z0-9_])\"");
        var used = Directory.EnumerateFiles(Path.Combine(RepositoryFiles.Root, "src", "TMConcept.Desktop"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .SelectMany(f => pattern.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);
        used.Where(k => !en.Contains(k)).ShouldBeEmpty();
    }

    [GeneratedRegex(@"\{DynamicResource ([\w.\-]+)\}")]
    private static partial Regex ResourceKey();
}

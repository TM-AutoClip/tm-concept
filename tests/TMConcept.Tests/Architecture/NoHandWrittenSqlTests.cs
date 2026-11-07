using System.Text.RegularExpressions;
using Shouldly;

namespace TMConcept.Tests.Architecture;

/// <summary>
/// No SQL is written by hand, anywhere (app and tests): every statement comes from EF Core (LINQ, SaveChanges, ExecuteUpdate/ExecuteDelete,
/// migration operations). 0.1.3–0.1.6 wrote SQL by hand; 0.1.7 went back to EF Core. Checked by the APIs that run SQL text and by SQL
/// statements in string literals.
/// </summary>
public sealed partial class NoHandWrittenSqlTests
{
    private const string ThisFile = "tests/TMConcept.Tests/Architecture/NoHandWrittenSqlTests.cs";

    [Fact]
    public void NoSourceFileRunsOrHoldsHandWrittenSql()
    {
        var files = RepositoryFiles.Sources("*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(RepositoryFiles.Root, "tests"), "*.cs", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(RepositoryFiles.Root, p).Replace('\\', '/'))
                .Where(p => !p.Contains("/bin/", StringComparison.Ordinal) && !p.Contains("/obj/", StringComparison.Ordinal)))
            .Where(p => p != ThisFile)
            .ToList();
        files.Count.ShouldBeGreaterThan(100);

        var found = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(Path.Combine(RepositoryFiles.Root, file));
            for (var i = 0; i < lines.Length; i++)
            {
                // Comments may name SQL (what an old version did); code may not.
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (SqlApi().Match(lines[i]) is { Success: true } api)
                {
                    found.Add($"{file}:{i + 1}: {api.Value}");
                }

                if (SqlText().Match(lines[i]) is { Success: true } text)
                {
                    found.Add($"{file}:{i + 1}: {text.Value}");
                }
            }
        }

        found.ShouldBeEmpty(string.Join(Environment.NewLine, found));
    }

    [Theory]
    [InlineData("db.Database.ExecuteSqlRaw(\"x\");")]
    [InlineData("command.CommandText = text;")]
    [InlineData("var c = \"SELECT COUNT(*) FROM x\";")]
    [InlineData("const string Up = \"\"\"ALTER TABLE \"a\" DROP COLUMN \"b\";\"\"\";")]
    [InlineData("Run(\"PRAGMA secure_delete = ON;\");")]
    public void ALineWithSqlIsFound(string line) => (SqlApi().IsMatch(line) || SqlText().IsMatch(line)).ShouldBeTrue();

    [Theory]
    [InlineData("e.HasNoKey().ToView(\"pragma_table_info\");")]
    [InlineData("await db.Concepts.Where(c => c.Number > 0).ExecuteDeleteAsync(cancellationToken);")]
    [InlineData("throw new InvalidOperationException(\"Select a profile first.\");")]
    [InlineData("runtimeEntityType.AddAnnotation(\"Relational:SqlQuery\", null);")]
    public void AnEfCoreLineIsNotFound(string line) => (SqlApi().IsMatch(line) || SqlText().IsMatch(line)).ShouldBeFalse();

    // A call of an API that runs SQL text (EF Core's compiled model names the annotation "Relational:SqlQuery": not a call).
    [GeneratedRegex(@"\b(FromSqlRaw|FromSqlInterpolated|FromSql|ExecuteSqlRaw|ExecuteSqlRawAsync|ExecuteSqlInterpolated|ExecuteSqlInterpolatedAsync|ExecuteSql|ExecuteSqlAsync|SqlQueryRaw|SqlQuery|CreateCommand)\s*[<(]|\.CommandText\b|\b(SqliteCommand|SqlOperation)\b")]
    private static partial Regex SqlApi();

    [GeneratedRegex(@"""[^""]*\b(SELECT\s|INSERT\s+(OR\s+\w+\s+)?INTO|UPDATE\s+\S+\s+SET|DELETE\s+FROM|CREATE\s+(UNIQUE\s+)?(TABLE|INDEX)|DROP\s+(TABLE|INDEX)|ALTER\s+TABLE|PRAGMA\s|VACUUM)")]
    private static partial Regex SqlText();
}

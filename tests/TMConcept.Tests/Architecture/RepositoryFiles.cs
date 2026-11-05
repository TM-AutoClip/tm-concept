namespace TMConcept.Tests.Architecture;

/// <summary>Finds the repository root (the folder of TMConcept.slnx) and lists its source files.</summary>
internal static class RepositoryFiles
{
    /// <summary>The repository root.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Source files under <c>src/</c> as repository-relative paths with forward slashes.</summary>
    public static IReadOnlyList<string> Sources(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), pattern, SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Root, p).Replace('\\', '/'))
            .Where(p => !p.Contains("/bin/", StringComparison.Ordinal) && !p.Contains("/obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TMConcept.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("TMConcept.slnx was not found above the test folder.");
    }
}

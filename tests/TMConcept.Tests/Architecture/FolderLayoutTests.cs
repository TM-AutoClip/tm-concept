using Shouldly;

namespace TMConcept.Tests.Architecture;

/// <summary>Every source file under <c>src/</c> sits in the category folder of its type (FOLDER1).</summary>
public sealed class FolderLayoutTests
{
    [Fact]
    public void SourceFilesAreInTheirCategoryFolder()
    {
        var files = RepositoryFiles.Sources("*.cs")
            .Select(p => (p, File.ReadAllText(Path.Combine(RepositoryFiles.Root, p))))
            .ToList();
        var violations = FolderLayoutRules.Analyze(files, p => File.Exists(Path.Combine(RepositoryFiles.Root, p)));
        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("public interface IThing { }", "Abstract")]
    [InlineData("public enum Kind { A }", "Constant")]
    [InlineData("public abstract class ThingBase { }", "Base")]
    [InlineData("public sealed record Thing(int A);", "Model")]
    [InlineData("public sealed class Thing\n{\n    public int A { get; set; }\n}", "Model")]
    [InlineData("public static class Limits\n{\n    public const int A = 1;\n}", "Constant")]
    [InlineData("public sealed class Service\n{\n    public void Run() { }\n}", "Concrete")]
    public void KindIsDetected(string text, string expected) => FolderLayoutRules.Kind(text).ShouldBe(expected);
}

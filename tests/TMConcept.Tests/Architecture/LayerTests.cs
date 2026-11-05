using System.Xml.Linq;
using Shouldly;

namespace TMConcept.Tests.Architecture;

/// <summary>
/// Layer boundaries: Core references no project of ours; Providers, Engines, Speech and Storage reference Core only; only the Desktop
/// composition root references them all. A new reference across the boundary fails here.
/// </summary>
public sealed class LayerTests
{
    public static TheoryData<string, string[]> Allowed() => new()
    {
        { "TMConcept.Core", [] },
        { "TMConcept.Providers", ["TMConcept.Core"] },
        { "TMConcept.Engines", ["TMConcept.Core"] },
        { "TMConcept.Speech", ["TMConcept.Core"] },
        { "TMConcept.Storage", ["TMConcept.Core"] },
        { "TMConcept.Desktop", ["TMConcept.Core", "TMConcept.Providers", "TMConcept.Engines", "TMConcept.Speech", "TMConcept.Storage"] },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void ProjectReferencesStayInsideTheLayer(string project, string[] allowed)
    {
        var path = Path.Combine(RepositoryFiles.Root, "src", project, project + ".csproj");
        var references = XDocument.Load(path).Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();
        references.ShouldBe(allowed.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void CoreHasNoUiStorageOrHttpFrameworkPackage()
    {
        var path = Path.Combine(RepositoryFiles.Root, "src", "TMConcept.Core", "TMConcept.Core.csproj");
        var packages = XDocument.Load(path).Descendants("PackageReference").Select(e => e.Attribute("Include")!.Value).ToArray();
        packages.ShouldBe(["Microsoft.Extensions.Logging.Abstractions"]);
    }
}

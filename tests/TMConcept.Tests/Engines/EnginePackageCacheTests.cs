using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Engines.Engines;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// 0.1.37: npm's cache and the temporary files of an engine package stay inside the package's own folder (the storage page counts them, the
/// full delete takes them); before, npm's cache went to <c>%LOCALAPPDATA%\npm-cache</c> where nothing ever removed it.
/// </summary>
public sealed class EnginePackageCacheTests
{
    [Theory]
    [InlineData("remotion")]
    [InlineData("hyperframes")]
    [InlineData("revideo")]
    public void NpmsCacheAndTheTempFolderAreInsideThePackageFolder(string name)
    {
        using var paths = new TempPaths();
        var package = new EnginePackage(name, paths, null!, null!, NullLogger.Instance);
        var root = Path.Combine(paths.ToolsDirectory, "engines", name);

        package.Root.ShouldBe(root);
        package.NpmCache.ShouldBe(Path.Combine(root, "npm-cache"));
        package.TempFolder.ShouldBe(Path.Combine(root, "tmp"));

        var environment = package.PackageEnvironment();
        environment["npm_config_cache"].ShouldBe(Path.Combine(root, "npm-cache"));
        environment[EnginePackage.NpmCacheVariable].ShouldStartWith(paths.ToolsDirectory, Case.Insensitive, "never the user's own npm cache");
        environment["TEMP"].ShouldBe(Path.Combine(root, "tmp"));
        environment["TMP"].ShouldBe(Path.Combine(root, "tmp"));
        environment["NPM_CONFIG_UPDATE_NOTIFIER"].ShouldBe("false");
    }
}

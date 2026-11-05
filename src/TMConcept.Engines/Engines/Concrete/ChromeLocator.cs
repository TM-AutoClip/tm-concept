using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Engines;

/// <summary>
/// The shared Chrome Headless Shell of the engines: the app's copy (<c>&lt;tools&gt;/chrome</c>), Remotion's own copy, or (Linux, where
/// Puppeteer has no ARM64 build) the system's Chromium. A render never downloads a browser by itself.
/// </summary>
public sealed partial class ChromeLocator(IAppPaths paths, IToolPlatform platform, ILogger<ChromeLocator> logger)
{
    /// <summary>The app's browser folder.</summary>
    public string Root => Path.Combine(paths.ToolsDirectory, "chrome");

    /// <summary>Remotion's own browser folder (its <c>ensureBrowser</c> downloads there).</summary>
    public string RemotionRoot => Path.Combine(paths.ToolsDirectory, "engines", "remotion", "node_modules", ".remotion");

    /// <summary>The browser, or null.</summary>
    public string? Find() => Search(Root, RemotionRoot) ?? SystemBrowser();

    /// <summary>
    /// Remotion's own Chrome Headless Shell, or null. Remotion renders only with this one (passed to it explicitly): given no browser it would
    /// download one during the render.
    /// </summary>
    public string? FindRemotion() => Search(RemotionRoot);

    private string? Search(params string[] roots)
    {
        var shell = platform.Executable("chrome-headless-shell");
        foreach (var root in roots)
        {
            try
            {
                if (Directory.Exists(root) && Directory.EnumerateFiles(root, shell, SearchOption.AllDirectories).FirstOrDefault() is { } found)
                {
                    return found;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "The browser folder could not be read");
            }
        }

        return null;
    }

    private string? SystemBrowser()
    {
        if (platform.UseSystemPrograms)
        {
            foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome" })
            {
                foreach (var folder in (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = Path.Combine(folder, name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Installs Chrome Headless Shell with <c>@puppeteer/browsers</c> from an engine package, at the version its locked Puppeteer expects
    /// (read from the package; "stable" when it cannot be read). Network; only after the user agreed.
    /// </summary>
    public async Task InstallAsync(EnginePackage package, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (Find() is not null)
        {
            return;
        }

        var cli = Path.Combine(package.Root, "node_modules", "@puppeteer", "browsers", "lib", "cjs", "main-cli.js");
        if (!File.Exists(cli))
        {
            cli = Path.Combine(package.Root, "node_modules", "@puppeteer", "browsers", "lib", "main-cli.js");
        }

        if (!File.Exists(cli))
        {
            throw new ToolInstallException("@puppeteer/browsers is missing from the engine packages.") { Code = "npm" };
        }

        Directory.CreateDirectory(Root);
        progress?.Report(new ToolProgress(0.6, "chrome-headless-shell"));
        var version = PinnedVersion(package.Root);
        var result = await package.RunNodeAsync([cli, "install", "chrome-headless-shell@" + version, "--path", Root],
            line => logger.LogDebug("chrome: {Line}", EnginePackage.Short(line)), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || Find() is null)
        {
            throw new ToolInstallException($"Chrome Headless Shell could not be installed ({result.ExitCode}): {EnginePackage.Short(result.LastLines)}")
            {
                Code = "download",
            };
        }

        logger.LogInformation("Chrome Headless Shell {Version} installed", version);
    }

    /// <summary>The Chrome version the locked puppeteer-core expects.</summary>
    public static string PinnedVersion(string packageRoot)
    {
        foreach (var file in new[] { "lib/puppeteer/revisions.js", "lib/cjs/puppeteer/revisions.js", "lib/esm/puppeteer/revisions.js" })
        {
            var path = Path.Combine(packageRoot, "node_modules", "puppeteer-core", file);
            if (File.Exists(path) && Revision().Match(File.ReadAllText(path)) is { Success: true } match)
            {
                return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            }
        }

        return "stable";
    }

    [GeneratedRegex(@"['""]?chrome-headless-shell['""]?\s*:\s*['""](\d+\.\d+\.\d+\.\d+)['""]|\bchrome\s*:\s*['""](\d+\.\d+\.\d+\.\d+)['""]")]
    private static partial Regex Revision();
}

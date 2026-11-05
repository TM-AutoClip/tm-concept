using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Engines;

/// <summary>
/// The npm packages of an engine: package.json + package-lock.json (pinned, only registry.npmjs.org) and the app's scripts are embedded and
/// unpacked into <c>&lt;tools&gt;/engines/&lt;name&gt;</c>. Install = <c>npm ci --ignore-scripts</c> with the app's own Node (package install
/// scripts never run). A marker keeps the SHA-256 of the lock file the install was made with; a new app version with a new lock needs a
/// new install.
/// </summary>
public sealed class EnginePackage(string name, IAppPaths paths, ToolInstaller tools, IProcessRunner runner, ILogger logger)
{
    private const string Marker = ".installed";
    private static readonly string[] FileNames = ["package.json", "package-lock.json", "render.mjs"];

    /// <summary>Package folder.</summary>
    public string Root => Path.Combine(paths.ToolsDirectory, "engines", name);

    /// <summary>Embedded files (name → resource).</summary>
    public IReadOnlyDictionary<string, string> Files { get; } = FileNames
        .Select(f => (File: f, Resource: $"TMConcept.Packages.{name}.{f}"))
        .Where(x => typeof(EnginePackage).Assembly.GetManifestResourceInfo(x.Resource) is not null)
        .ToDictionary(x => x.File, x => x.Resource, StringComparer.Ordinal);

    /// <summary>SHA-256 of the embedded lock file.</summary>
    public string LockSha => Convert.ToHexStringLower(SHA256.HashData(Read(Files["package-lock.json"])));

    /// <summary>Whether the packages are installed with today's lock and <paramref name="relativeFile"/> exists.</summary>
    public bool Ready(string relativeFile)
    {
        var marker = Path.Combine(Root, Marker);
        return File.Exists(Path.Combine(Root, relativeFile)) && File.Exists(marker) && File.ReadAllText(marker).Trim() == LockSha
            && Files.All(f => Same(Path.Combine(Root, f.Key), f.Value));
    }

    /// <summary>Unpacks the embedded files (only those that differ). No network.</summary>
    public void Extract()
    {
        Directory.CreateDirectory(Root);
        foreach (var (file, resource) in Files)
        {
            var target = Path.Combine(Root, file);
            if (!Same(target, resource))
            {
                File.WriteAllBytes(target, Read(resource));
            }
        }
    }

    /// <summary>npm ci without install scripts. Network (registry.npmjs.org).</summary>
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var node = tools.Require(ToolKind.Node);
        var npm = NpmCli(node) ?? throw new ToolInstallException("npm was not found next to Node.") { Code = "npm" };
        Extract();
        var temp = Path.Combine(Root, "tmp");
        Directory.CreateDirectory(temp);
        progress?.Report(new ToolProgress(0.05, "npm ci " + name));
        var result = await runner.RunAsync(new ProcessStart(node, [npm, "ci", "--ignore-scripts", "--no-audit", "--no-fund", "--loglevel=error"], Root)
        {
            Environment = Environment(temp),
        }, line => logger.LogDebug("npm {Engine}: {Line}", name, Short(line)), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ToolInstallException($"npm ci failed ({result.ExitCode}): {Short(result.LastLines)}") { Code = "npm" };
        }

        await File.WriteAllTextAsync(Path.Combine(Root, Marker), LockSha, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Engine packages {Engine} installed", name);
    }

    /// <summary>Runs a Node script of the package (browser installs and similar).</summary>
    public async Task<ProcessResult> RunNodeAsync(IReadOnlyList<string> arguments, Action<string>? onLine, CancellationToken cancellationToken)
    {
        var node = tools.Require(ToolKind.Node);
        var temp = Path.Combine(Root, "tmp");
        Directory.CreateDirectory(temp);
        return await runner.RunAsync(new ProcessStart(node, arguments, Root) { Environment = Environment(temp) }, onLine, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The environment of engine processes: temp inside the folder, telemetry and update checks off.</summary>
    public static Dictionary<string, string> Environment(string temp) => new(StringComparer.Ordinal)
    {
        ["TEMP"] = temp,
        ["TMP"] = temp,
        ["TMPDIR"] = temp,
        ["DO_NOT_TRACK"] = "1",
        ["DISABLE_TELEMETRY"] = "true",
        ["HYPERFRAMES_NO_TELEMETRY"] = "1",
        ["HYPERFRAMES_NO_UPDATE_CHECK"] = "1",
        ["HYPERFRAMES_NO_OPTIONAL_INSTALL"] = "1",
        ["HYPERFRAMES_SKIP_SKILLS"] = "1",
        ["NPM_CONFIG_UPDATE_NOTIFIER"] = "false",
        ["NODE_OPTIONS"] = string.Empty,
    };

    /// <summary>One log line: short, single line (engine output is not trusted).</summary>
    public static string Short(string? text)
    {
        var line = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length <= 400 ? line : line[..400] + "…";
    }

    private static string? NpmCli(string node)
    {
        var folder = Path.GetDirectoryName(node)!;
        string[] candidates =
        [
            Path.Combine(folder, "node_modules", "npm", "bin", "npm-cli.js"),
            Path.Combine(folder, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js"),
        ];
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static byte[] Read(string resource)
    {
        using var stream = typeof(EnginePackage).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Embedded file is missing: " + resource);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static bool Same(string path, string resource) => File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(Read(resource));
}

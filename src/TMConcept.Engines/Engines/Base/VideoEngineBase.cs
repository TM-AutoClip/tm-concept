using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Processes;
using TMConcept.Core.Render;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Engines;

/// <summary>
/// Shared engine work: code check, safe writes (only inside the work folder), install (packages + browser), and the Node process with
/// progress lines. The process environment is cleaned of secrets by the process runner; temp files stay in the work folder.
/// </summary>
public abstract class VideoEngineBase(EnginePackage package, ChromeLocator chrome, ToolInstaller tools, IProcessRunner runner, ILogger logger) : IVideoEngine
{
    /// <summary>The input file name in the work folder.</summary>
    public const string InputFile = "input.json";

    /// <inheritdoc />
    public abstract EngineKind Kind { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public virtual bool Experimental => false;

    /// <inheritdoc />
    public virtual bool NeedsLicenseConsent => false;

    /// <inheritdoc />
    public abstract string CodeFile { get; }

    /// <inheritdoc />
    public abstract string Contract { get; }

    /// <inheritdoc />
    public abstract string SampleCode { get; }

    /// <summary>The packages.</summary>
    protected EnginePackage Package { get; } = package;

    /// <summary>The folder of the engine's packages (its size and what Remove takes away).</summary>
    public string PackageRoot => Package.Root;

    /// <summary>The browser.</summary>
    protected ChromeLocator Chrome { get; } = chrome;

    /// <summary>Logger.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>A file whose presence means the packages are unpacked (relative to the package folder).</summary>
    protected abstract string PackageProbe { get; }

    /// <inheritdoc />
    public virtual EngineStatus Status()
    {
        var missing = new List<string>();
        if (!tools.Status(ToolKind.Node).Ready)
        {
            missing.Add("node");
        }

        if (!tools.Status(ToolKind.Ffmpeg).Ready)
        {
            missing.Add("ffmpeg");
        }

        if (!Package.Ready(PackageProbe))
        {
            missing.Add("packages");
        }

        if (Browser() is null)
        {
            missing.Add("browser");
        }

        return new EngineStatus(missing.Count == 0, missing);
    }

    /// <inheritdoc />
    public virtual async Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (!agreed)
        {
            throw new InvalidOperationException("An engine is installed only after the user agreed.");
        }

        // Packages that are installed with today's lock are not downloaded again (only the browser may be missing).
        if (!Package.Ready(PackageProbe))
        {
            await Package.InstallAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        await InstallBrowserAsync(progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(new ToolProgress(1, Name));
    }

    /// <inheritdoc />
    public void WriteProject(EngineRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var findings = EngineCodeCheck.Check(Kind, request.Code);
        if (findings.Count > 0)
        {
            throw new EngineException($"{Name} code did not pass the safety check: " + string.Join(" · ", findings.Take(6))) { Code = "check", Findings = findings };
        }

        Directory.CreateDirectory(request.Folder);
        foreach (var (relative, content) in Skeleton(request))
        {
            SafeWrite(request.Folder, relative, content);
        }

        SafeWrite(request.Folder, CodeFile, request.Code);
        SafeWrite(request.Folder, InputFile, RenderInputBuilder.ToJson(request.Input));
    }

    /// <inheritdoc />
    public abstract Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken);

    /// <summary>The app's files of the project (relative path → content). The model cannot touch them.</summary>
    protected abstract IReadOnlyDictionary<string, string> Skeleton(EngineRenderRequest request);

    /// <summary>The browser this engine renders with (the shared one by default), or null when it is not installed.</summary>
    protected virtual string? Browser() => Chrome.Find();

    /// <summary>Installs the browser (shared Chrome Headless Shell by default).</summary>
    protected virtual Task InstallBrowserAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
        Chrome.InstallAsync(Package, progress, cancellationToken);

    /// <summary>Throws when the engine is not ready.</summary>
    protected void RequireReady()
    {
        var status = Status();
        if (!status.Ready)
        {
            throw new EngineException($"{Name} is not installed (missing: {string.Join(", ", status.Missing)}). It is installed when a video is produced (after a confirmation).") { Code = "missing" };
        }
    }

    /// <summary>
    /// Runs Node: lines become progress (<see cref="EngineProgressParser"/>); an "error" event or a non-zero exit is an <see cref="EngineException"/>.
    /// ffmpeg's folder goes first on PATH so engines that call "ffmpeg" use the app's copy.
    /// </summary>
    protected async Task RunNodeAsync(IReadOnlyList<string> arguments, string workingDirectory, string workFolder, IReadOnlyDictionary<string, string>? extra,
        IProgress<EngineEvent>? progress, bool requireDone, CancellationToken cancellationToken)
    {
        var node = tools.Require(ToolKind.Node);
        string? error = null;
        var done = false;
        var temp = Path.Combine(workFolder, "tmp");
        Directory.CreateDirectory(temp);
        var environment = EnginePackage.Environment(temp);
        var ffmpeg = tools.Require(ToolKind.Ffmpeg);
        environment["PATH"] = Path.GetDirectoryName(ffmpeg) + Path.PathSeparator + (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        foreach (var (name, value) in extra ?? new Dictionary<string, string>())
        {
            environment[name] = value;
        }

        void Line(string line)
        {
            if (EngineProgressParser.Parse(line) is not { } item)
            {
                Logger.LogDebug("{Engine}: {Line}", Name, EnginePackage.Short(line));
                return;
            }

            error ??= item.Error;
            done |= item.Done;
            if (item.Ratio is not null || item.Done)
            {
                progress?.Report(item);
            }
        }

        var result = await runner.RunAsync(new ProcessStart(node, arguments, workingDirectory) { Environment = environment }, Line, cancellationToken)
            .ConfigureAwait(false);
        if (error is not null)
        {
            throw new EngineException($"{Name}: {error}") { Code = "render" };
        }

        if (result.ExitCode != 0 || (requireDone && !done))
        {
            throw new EngineException($"{Name} ended with code {result.ExitCode}: {ChildOutput.Summary(result.LastLines)}") { Code = "render" };
        }
    }

    /// <summary>Writes only inside the work folder (".." cannot leave it).</summary>
    protected static void SafeWrite(string folder, string relative, string content)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException("Writing outside the work folder was refused: " + relative) { Code = "check" };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content, new UTF8Encoding(false));
    }
}

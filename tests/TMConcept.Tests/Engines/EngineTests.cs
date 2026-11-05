using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Engines;
using TMConcept.Core.Processes;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Processes;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

public sealed class EngineTests
{
    private static IVideoEngine[] All(TempPaths paths)
    {
        IProcessRunner runner = new ProcessRunner(ChildProcessTests.Guard(), NullLogger<ProcessRunner>.Instance);
        var platform = new WindowsToolPlatform(runner);
        var tools = new ToolInstaller(paths, platform, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance),
            NullLogger<ToolInstaller>.Instance);
        var chrome = new ChromeLocator(paths, platform, NullLogger<ChromeLocator>.Instance);
        return
        [
            new HyperFramesEngine(paths, tools, chrome, runner, NullLogger<HyperFramesEngine>.Instance),
            new RemotionEngine(paths, tools, chrome, runner, NullLogger<RemotionEngine>.Instance),
            new RevideoEngine(paths, tools, chrome, runner, NullLogger<RevideoEngine>.Instance),
        ];
    }

    [Fact]
    public void EverySampleCodePassesTheSafetyCheckAndIsWrittenOnlyInsideTheWorkFolder()
    {
        using var paths = new TempPaths();
        foreach (var engine in All(paths))
        {
            EngineCodeCheck.Check(engine.Kind, engine.SampleCode).ShouldBeEmpty(engine.Name);
            var folder = Path.Combine(paths.WorkDirectory, engine.Name);
            engine.WriteProject(new EngineRenderRequest(folder, engine.SampleCode, SampleRender.Input(), Path.Combine(folder, "out.mp4"), 1));
            File.Exists(Path.Combine(folder, engine.CodeFile)).ShouldBeTrue();
            File.ReadAllText(Path.Combine(folder, "input.json")).ShouldContain("\"scenes\"");
        }
    }

    [Fact]
    public void RejectedCodeWritesNothing()
    {
        using var paths = new TempPaths();
        var engine = All(paths)[0];
        var folder = Path.Combine(paths.WorkDirectory, "rejected");
        var code = engine.SampleCode + "\nfetch('https://example.com');";

        var error = Should.Throw<EngineException>(() => engine.WriteProject(new EngineRenderRequest(folder, code, SampleRender.Input(), "x.mp4", 1)));

        error.Code.ShouldBe("check");
        error.Findings.ShouldNotBeEmpty();
        Directory.Exists(folder).ShouldBeFalse();
    }

    [Fact]
    public void TheHyperFramesPageKeepsTheInputInsideItsScriptBlock()
    {
        var input = SampleRender.Input();
        input.Title = "</script><script>alert(1)</script>";

        var html = HyperFramesEngine.IndexHtml(input);

        html.ShouldNotContain("</script><script>alert");
        html.ShouldContain("Content-Security-Policy");
    }

    [Fact]
    public void EveryEnginePackageIsEmbeddedWithALockFromTheOfficialRegistry()
    {
        using var paths = new TempPaths();
        foreach (var engine in All(paths))
        {
            var package = new EnginePackage(engine.Kind.ToString().ToLowerInvariant(), paths, null!, null!, NullLogger.Instance);
            package.Files.Keys.ShouldContain("package.json");
            package.Files.Keys.ShouldContain("package-lock.json");
            package.LockSha.Length.ShouldBe(64);
        }

        foreach (var file in Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "TMConcept.Engines", "Packages"), "package-lock.json", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file).Where(l => l.Contains("\"resolved\"", StringComparison.Ordinal)))
            {
                line.ShouldContain("https://registry.npmjs.org/");
            }
        }
    }

    [Fact]
    public async Task SampleRenderWhenTheToolsAreInstalled()
    {
        // A real local render (no network, no GPU). Runs only when TMCONCEPT_RENDER_DATA points to a data folder whose tools are installed.
        var data = Environment.GetEnvironmentVariable("TMCONCEPT_RENDER_DATA");
        if (string.IsNullOrEmpty(data))
        {
            Assert.Skip("TMCONCEPT_RENDER_DATA is not set.");
        }

        var paths = new FixedDataPaths(data);
        foreach (var engine in All(paths.Inner).Where(e => e.Status().Ready))
        {
            var video = await new SampleRender(paths.Inner).RenderAsync(engine, null, TestContext.Current.CancellationToken);
            new FileInfo(video).Length.ShouldBeGreaterThan(10_000, engine.Name);
        }
    }

    private static string RepositoryRoot()
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "TMConcept.slnx")))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        return folder;
    }

    private sealed class FixedDataPaths(string data)
    {
        public TempPaths Inner { get; } = TempPaths.At(data);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.DiskSpace;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.37: the start-up sweep on a real data folder: what an earlier version's reinstall left next to <c>tools</c> (its old folders and its
/// marker file), the staging folders of installs and the engines' tmp folders go for good without a question (28 Nov 2026: also without the
/// components folder and after a cut-short reinstall); <c>tools</c> itself, folders of another name and the installed files stay; nothing
/// while an install runs.
/// </summary>
public sealed class LeftoverSweeperTests
{
    private static string Folder(string path, int bytes = 10)
    {
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "f.bin"), new byte[bytes]);
        return path;
    }

    private static (string Old, string Failed, string Other, string Temp, string Installed) Layout(DesktopHarness h)
    {
        var data = h.Paths.DataDirectory;
        Folder(Path.Combine(h.Paths.ToolsDirectory, "uv"));
        var engine = Path.Combine(h.Paths.ToolsDirectory, "engines", "remotion");
        return (Folder(Path.Combine(data, "tools.old-20261116-120000"), 1000), Folder(Path.Combine(data, "tools.failed-20261116-120501"), 500),
            Folder(Path.Combine(data, "tools.mine")), Folder(Path.Combine(engine, "tmp"), 24), Folder(Path.Combine(engine, "node_modules")));
    }

    /// <summary>Keeps the level of every line the sweeper writes.</summary>
    private sealed class LevelLogger : ILogger<LeftoverSweeper>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    [Fact]
    public async Task TheOldComponentsFoldersAndTheEngineTempsGoTheRestStays()
    {
        using var h = new DesktopHarness();
        var (old, failed, other, temp, installed) = Layout(h);

        var freed = await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None);

        freed.ShouldBe(1524);
        Directory.Exists(old).ShouldBeFalse();
        Directory.Exists(failed).ShouldBeFalse();
        Directory.Exists(temp).ShouldBeFalse();
        Directory.Exists(other).ShouldBeTrue("a folder of another name is never taken");
        Directory.Exists(installed).ShouldBeTrue("the installed packages stay");
        File.Exists(Path.Combine(h.Paths.ToolsDirectory, "uv", "f.bin")).ShouldBeTrue("the components folder itself is never touched");
        (await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None)).ShouldBe(0, "nothing is left the second time");
    }

    [Fact]
    public async Task NothingIsTouchedWhileAComponentInstallRuns()
    {
        using var h = new DesktopHarness();
        var (old, failed, _, temp, _) = Layout(h);
        var gate = h.Get<IComponentWorkGate>();

        using (gate.Install())
        {
            (await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None)).ShouldBe(0);
        }

        Directory.Exists(old).ShouldBeTrue();
        Directory.Exists(failed).ShouldBeTrue();
        Directory.Exists(temp).ShouldBeTrue();
    }

    [Fact]
    public async Task TheOldReinstallMarkerAndTheStagingFoldersGoWithoutAQuestion()
    {
        using var h = new DesktopHarness();
        var (old, failed, _, _, installed) = Layout(h);
        var marker = h.Paths.ToolsDirectory + ".reinstalling";
        File.WriteAllText(marker, "an earlier version's cut-short reinstall");
        var part = Folder(Path.Combine(h.Paths.ToolsDirectory, "downloads"), 100);
        var unpack = Folder(Path.Combine(h.Paths.ToolsDirectory, "ffmpeg", "7.1.unpack"), 50);
        var tool = Folder(Path.Combine(h.Paths.ToolsDirectory, "ffmpeg", "7.1"), 7);

        await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None);

        File.Exists(marker).ShouldBeFalse();
        Directory.Exists(old).ShouldBeFalse("an earlier version's old folder goes even after a cut-short reinstall");
        Directory.Exists(failed).ShouldBeFalse();
        Directory.Exists(part).ShouldBeFalse("unfinished downloads are deleted; the component is downloaded again from the start");
        Directory.Exists(unpack).ShouldBeFalse();
        File.Exists(Path.Combine(tool, "f.bin")).ShouldBeTrue("an installed version stays");
        Directory.Exists(installed).ShouldBeTrue();
    }

    [Fact]
    public async Task WithoutTheComponentsFolderTheOldFoldersGoToo()
    {
        using var h = new DesktopHarness();
        var (old, failed, _, _, _) = Layout(h);
        Directory.Delete(h.Paths.ToolsDirectory, recursive: true);

        (await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None)).ShouldBe(1500);
        Directory.Exists(old).ShouldBeFalse("the components they held are downloaded again through the install journal");
        Directory.Exists(failed).ShouldBeFalse();
    }

    [Fact]
    public async Task ANewDataFolderWithoutComponentsWritesNoWarning()
    {
        var logger = new LevelLogger();
        using var h = new DesktopHarness(s => s.AddSingleton<ILogger<LeftoverSweeper>>(logger));
        if (Directory.Exists(h.Paths.ToolsDirectory))
        {
            Directory.Delete(h.Paths.ToolsDirectory, recursive: true);
        }

        (await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None)).ShouldBe(0);
        logger.Levels.ShouldNotContain(LogLevel.Warning, "nothing is kept, so nothing to warn about");
    }

    [Fact]
    public async Task AMusicPageWorkFolderAClosedAppLeftIsRemoved()
    {
        // 0.1.59: work/music-make/<piece> of a stopped or closed run (no piece is being made at start).
        using var h = new DesktopHarness();
        var left = Folder(Path.Combine(h.Paths.WorkDirectory, "music-make", "e5152260", "out"), 7);

        await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None);

        Directory.Exists(Path.GetDirectoryName(left)).ShouldBeFalse();
        Directory.Exists(Path.Combine(h.Paths.WorkDirectory, "music-make")).ShouldBeTrue("only the pieces' folders go");
    }
}


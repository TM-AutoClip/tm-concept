using Shouldly;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.DiskSpace;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 0.1.37: the start-up sweep on a real data folder: what a cut-short reinstall left next to <c>tools</c> and the engines' tmp folders go for
/// good; <c>tools</c> itself, folders of another name and the engines' installed files stay; nothing while an install runs; after a
/// reinstall that was cut short the old folders stay for one start.
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

        using (gate.Reinstall())
        {
            File.Exists(gate.MarkerFile).ShouldBeTrue();
            gate.Interrupted.ShouldBeFalse("this process's own reinstall is not a cut-short one");
            (await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None)).ShouldBe(0);
        }

        File.Exists(gate.MarkerFile).ShouldBeFalse("the marker goes when the reinstall ends");
        Directory.Exists(old).ShouldBeTrue();
        Directory.Exists(failed).ShouldBeTrue();
        Directory.Exists(temp).ShouldBeTrue();
    }

    [Fact]
    public async Task AfterACutShortReinstallTheOldFoldersStayForOneStart()
    {
        using var h = new DesktopHarness();
        var (old, failed, _, temp, _) = Layout(h);
        var gate = h.Get<IComponentWorkGate>();
        File.WriteAllText(gate.MarkerFile, "previous run");
        gate.Interrupted.ShouldBeTrue();

        (await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None)).ShouldBe(24, "only the engine's tmp");
        Directory.Exists(old).ShouldBeTrue("it may hold the components that worked");
        Directory.Exists(failed).ShouldBeTrue();
        Directory.Exists(temp).ShouldBeFalse();
        File.Exists(gate.MarkerFile).ShouldBeFalse("the next start sweeps them");

        (await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None)).ShouldBe(1500);
        Directory.Exists(old).ShouldBeFalse();
        Directory.Exists(failed).ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutTheComponentsFolderTheOldFoldersStay()
    {
        using var h = new DesktopHarness();
        var (old, failed, _, _, _) = Layout(h);
        Directory.Delete(h.Paths.ToolsDirectory, recursive: true);

        (await h.Get<ILeftoverSweeper>().SweepAsync(CancellationToken.None)).ShouldBe(0);
        Directory.Exists(old).ShouldBeTrue("it may be the only copy of the components");
        Directory.Exists(failed).ShouldBeTrue();
    }

    [Fact]
    public void AReinstallWhoseOldFolderCouldNotGoBackKeepsTheMarker()
    {
        using var h = new DesktopHarness();
        var gate = h.Get<IComponentWorkGate>();

        gate.KeepMarker();
        gate.Reinstall().Dispose();

        File.Exists(gate.MarkerFile).ShouldBeFalse("a keep outside a reinstall does nothing");

        using (gate.Reinstall())
        {
            gate.KeepMarker();
        }

        File.Exists(gate.MarkerFile).ShouldBeTrue();
        gate.Interrupted.ShouldBeTrue("the next start keeps the old folders once more");

        gate.ForgetInterrupted();
        gate.Reinstall().Dispose();

        File.Exists(gate.MarkerFile).ShouldBeFalse("the keep holds for that one reinstall only");
    }
}

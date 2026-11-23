using Shouldly;
using TMConcept.Core.DiskSpace;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.37: the start-up sweep decides from folder lists alone: only old components folders whose name fits exactly, only next to
/// <c>tools</c>, never <c>tools</c> itself, nothing while a reinstall runs; the engines' tmp folders only right under <c>tools/engines</c>.
/// </summary>
public sealed class LeftoverSweepTests
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "tmconcept-sweep-not-made", "data");

    private static readonly string Tools = Path.Combine(Data, "tools");

    private readonly LeftoverSweep _sweep = new();

    [Fact]
    public void OnlyTheOldAndFailedFoldersWhoseNameFitsExactlyAreSelected()
    {
        string[] folders =
        [
            Tools,
            Path.Combine(Data, "tools.old-20261116-120000"),
            Path.Combine(Data, "tools.failed-20261116-120501"),
            Path.Combine(Data, "tools.aside-20261116-120000"),
            Path.Combine(Data, "tools.old-20261116-120000-copy"),
            Path.Combine(Data, "tools.old-2026111-120000"),
            Path.Combine(Data, "tools.old-"),
            Path.Combine(Data, "tools.old"),
            Path.Combine(Data, "tools.reinstalling"),
            Path.Combine(Data, "toolsx.old-20261116-120000"),
            Path.Combine(Data, "backups"),
            Path.Combine(Data, "work", "tools.old-20261116-120000"),
            Path.Combine(Tools, "tools.old-20261116-120000"),
            Path.Combine(Path.GetTempPath(), "tools.old-20261116-120000"),
        ];

        _sweep.OldComponentFolders(Tools, folders, reinstalling: false, toolsPresent: true).ShouldBe(
        [
            Path.Combine(Data, "tools.failed-20261116-120501"),
            Path.Combine(Data, "tools.old-20261116-120000"),
        ]);
    }

    [Fact]
    public void NothingIsSelectedWhileAReinstallRuns()
    {
        string[] folders = [Path.Combine(Data, "tools.old-20261116-120000"), Path.Combine(Data, "tools.failed-20261116-120501")];
        _sweep.OldComponentFolders(Tools, folders, reinstalling: true, toolsPresent: true).ShouldBeEmpty();
    }

    [Fact]
    public void NothingIsSelectedWhileTheToolsFolderIsNotThere()
    {
        // A reinstall whose old folder could not go back (or a crash right after setting it aside): it may be the only copy of the components.
        string[] folders = [Path.Combine(Data, "tools.old-20261116-120000"), Path.Combine(Data, "tools.failed-20261116-120501")];
        _sweep.OldComponentFolders(Tools, folders, reinstalling: false, toolsPresent: false).ShouldBeEmpty();
    }

    [Fact]
    public void TheToolsFolderItselfIsNeverSelected()
    {
        _sweep.OldComponentFolders(Tools, [Tools, Tools + Path.DirectorySeparatorChar, Data], reinstalling: false, toolsPresent: true).ShouldBeEmpty();
        LeftoverSweep.Matches("tools", "tools").ShouldBeFalse();
        LeftoverSweep.Matches("tools", "tools.old-20261116-120000").ShouldBeTrue();
        LeftoverSweep.Matches("tools", "tools.failed-20261116-120000").ShouldBeTrue();
        LeftoverSweep.Matches("tools", "tools.old-20261116-12000a").ShouldBeFalse();
        LeftoverSweep.Matches(string.Empty, ".old-20261116-120000").ShouldBeFalse();
    }

    [Fact]
    public void TheEngineTempsAreOnlyThoseOfPackageFoldersRightUnderEngines()
    {
        var engines = Path.Combine(Tools, "engines");
        string[] folders = [Path.Combine(engines, "remotion"), Path.Combine(engines, "hyperframes"), Path.Combine(engines, "remotion", "node_modules"), Tools, engines];

        _sweep.EngineTemps(Tools, folders).ShouldBe([Path.Combine(engines, "hyperframes", "tmp"), Path.Combine(engines, "remotion", "tmp")]);
    }
}

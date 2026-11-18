using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// After a new app version (0.1.16): the installed components are updated, reinstalled from scratch or left for later; the version is recorded
/// in preferences.json only after an update or a reinstall that ended ready, or when nothing is installed. What was installed comes from the
/// install record (0.1.17, outside the components folder) and from folders that hold an install. Fake components, dialog and recycle bin:
/// nothing is downloaded or moved to the real recycle bin.
/// </summary>
public sealed class VersionChangeTests
{
    [Theory]
    [InlineData("0.1.16", "0.1.16", true, VersionChange.Same)]
    [InlineData("0.1.15", "0.1.16", true, VersionChange.Ask)]
    [InlineData("0.1.15", "0.1.16", false, VersionChange.Record)]
    [InlineData("", "0.1.16", true, VersionChange.Ask)]
    [InlineData("", "0.1.16", false, VersionChange.Record)]
    [InlineData(null, "0.1.16", false, VersionChange.Record)]
    [InlineData("0.1.17", "0.1.16", true, VersionChange.Ask)]
    public void TheVersionThatRanBeforeDecidesWhatHappens(string? recorded, string current, bool installed, VersionChange expected) =>
        VersionCheck.Decide(recorded, current, installed).ShouldBe(expected);

    [Fact]
    public void TheLastVersionIsKeptInThePreferencesWithoutResettingAnother()
    {
        using var h = new DesktopHarness();
        var file = h.Get<UiPreferencesFile>();
        file.Read().LastVersion.ShouldBeEmpty("0.1.15 and earlier did not record it");
        file.Change(p => p.Language = "tr");

        file.Change(p => p.LastVersion = "0.1.16");

        var read = file.Read();
        (read.LastVersion, read.Language).ShouldBe(("0.1.16", "tr"));
    }

    [AvaloniaFact]
    public async Task UpdateInstallsAgainOnlyWhatWasInstalledAndRecordsTheVersion()
    {
        var (h, used, unused, dialog, bin, confirm) = Setup(UpdateChoice.Update);
        using (h)
        {
            Preferences(h).Change(p => p.LastVersion = "0.1.15");

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.ShouldBe([("0.1.15", "0.1.16")]);
            dialog.Folders.Single().ShouldEndWith("tools");
            (used.Installs, unused.Installs).ShouldBe((1, 0), "only what was installed before, nothing else");
            bin.Moved.ShouldBeEmpty("an update moves nothing");
            confirm.Messages.ShouldHaveSingleItem().ShouldContain(used.Name);
            Preferences(h).Read().LastVersion.ShouldBe("0.1.16");

            // The next start with the same version asks nothing.
            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);
            dialog.Asked.Count.ShouldBe(1);
        }
    }

    [AvaloniaFact]
    public async Task LaterChangesNothingAndAsksAgainAtTheNextStart()
    {
        var (h, used, _, dialog, bin, _) = Setup(UpdateChoice.Later);
        using (h)
        {
            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);
            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.ShouldBe([(string.Empty, "0.1.16"), (string.Empty, "0.1.16")], "coming from 0.1.15: no version was recorded, components are there");
            used.Installs.ShouldBe(0);
            bin.Moved.ShouldBeEmpty();
            Preferences(h).Read().LastVersion.ShouldBeEmpty();
        }
    }

    [AvaloniaFact]
    public async Task ReinstallSetsTheFolderAsideInstallsAgainAndOnlyThenMovesTheOldFolderToTheRecycleBin()
    {
        var (h, used, unused, dialog, bin, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            var database = h.Paths.DatabaseFile;
            var aside = string.Empty;
            used.OnInstall = () =>
            {
                // The install runs while the old folder is set aside: nothing is in the recycle bin yet, the new folder starts empty.
                bin.Moved.ShouldBeEmpty("the old folder is removed only after the new install ended ready");
                aside = Directory.GetDirectories(h.Paths.DataDirectory, "tools" + TMConcept.Desktop.Components.ComponentReinstaller.AsidePrefix + "*").Single();
                Directory.Exists(h.Paths.ToolsDirectory).ShouldBeFalse();
                Directory.CreateDirectory(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "new"));
                File.WriteAllText(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "new", "sd-cli.exe"), "new");
            };

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.Count.ShouldBe(1);
            confirm.Asked.Count.ShouldBe(1, "one confirmation: the downloads");
            bin.Moved.ShouldBe([aside]);
            File.Exists(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "new", "sd-cli.exe")).ShouldBeTrue("the new install stays");
            (used.Installs, unused.Installs).ShouldBe((1, 0));
            Path.GetDirectoryName(database).ShouldBe(h.Paths.DataDirectory, "the database is outside the components folder");
            Preferences(h).Read().LastVersion.ShouldBe("0.1.16");
        }
    }

    [AvaloniaFact]
    public async Task ADeclinedReinstallPutsTheOldFolderBackAndTheNextStartAsksAgain()
    {
        var (h, used, _, dialog, bin, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            var marker = Path.Combine(used.Folder!, "sd-cli.exe");
            confirm.Answer = false;
            var text = h.Get<Localizer>();

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            File.Exists(marker).ShouldBeTrue("declined: the old components are back in their place");
            bin.Moved.ShouldBeEmpty();
            Directory.GetDirectories(h.Paths.DataDirectory).Where(d => Path.GetFileName(d).StartsWith("tools.", StringComparison.Ordinal)).ShouldBeEmpty();
            used.Installs.ShouldBe(0);
            Preferences(h).Read().LastVersion.ShouldBeEmpty();

            // The record outlives the folder: even with the components folder gone the next start asks again (0.1.16 recorded the version
            // silently because the folder that said what was installed had just been moved away).
            Directory.Delete(h.Paths.ToolsDirectory, recursive: true);
            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);
            dialog.Asked.Count.ShouldBe(2);
            confirm.Messages[^1].ShouldContain(used.Name);
            _ = text;
        }
    }

    [AvaloniaFact]
    public async Task ALibraryTheAppLoadedFromTheFolderStopsTheReinstallAndTheRemovalWithARestartHint()
    {
        var (h, used, _, _, bin, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            var marker = Path.Combine(used.Folder!, "sd-cli.exe");
            used.Ready = true;
            used.LoadedInProcess = true;
            var expected = h.Get<Localizer>().Format("components.loaded_restart", used.Name);

            var outcome = await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None);

            outcome.Ready.ShouldBeFalse();
            outcome.Reason.ShouldBe(expected);
            File.Exists(marker).ShouldBeTrue("nothing was moved");
            confirm.Asked.ShouldBeEmpty();
            used.Installs.ShouldBe(0);

            var cleaner = h.Get<TMConcept.Desktop.DiskSpace.IDiskCleaner>();
            var removal = await cleaner.RemoveAsync([cleaner.ItemOf(used)], CancellationToken.None);
            removal.Failed.ShouldHaveSingleItem().Reason.ShouldBe(expected);
            removal.Removed.ShouldBeEmpty();
            bin.Moved.ShouldBeEmpty();
            confirm.Asked.ShouldBeEmpty("no offer to delete it for good either");
            File.Exists(marker).ShouldBeTrue();
        }
    }

    [AvaloniaFact]
    public async Task AFailedReinstallPutsTheOldFolderBackAndDiscardsWhatTheNewInstallLeft()
    {
        var (h, used, _, _, bin, _) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            var marker = Path.Combine(used.Folder!, "sd-cli.exe");
            used.Fail = true;
            used.OnInstall = () => Directory.CreateDirectory(Path.Combine(h.Paths.ToolsDirectory, "downloads"));

            var outcome = await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None);

            outcome.Ready.ShouldBeFalse();
            outcome.Reason.ShouldEndWith(h.Get<Localizer>()["components.reinstall.kept"]);
            File.Exists(marker).ShouldBeTrue();
            Directory.Exists(Path.Combine(h.Paths.ToolsDirectory, "downloads")).ShouldBeFalse("the unfinished new folder is not the components folder");
            bin.Moved.ShouldHaveSingleItem().ShouldContain(TMConcept.Desktop.Components.ComponentReinstaller.FailedPrefix);
        }
    }

    [AvaloniaFact]
    public async Task WhenTheRecycleBinFailsTheOldFolderIsDeletedOnlyAfterItsOwnConfirmation()
    {
        var (h, used, _, _, bin, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            bin.Fail = true;
            confirm.Answers.Enqueue(true);
            confirm.Answers.Enqueue(false);
            var text = h.Get<Localizer>();

            var outcome = await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None);

            outcome.Ready.ShouldBeTrue("the new components are installed either way");
            confirm.Asked.ShouldBe([text["install.title"], text["components.reinstall.bin_failed.title"]]);
            var aside = Directory.GetDirectories(h.Paths.DataDirectory, "tools" + TMConcept.Desktop.Components.ComponentReinstaller.AsidePrefix + "*");
            aside.ShouldHaveSingleItem("declined: the old folder stays aside, nothing is deleted");
            used.Installs.ShouldBe(1);

            // Confirmed: the old folder is deleted for good.
            used.Ready = false;
            Directory.Delete(aside[0], recursive: true);
            confirm.Answers.Enqueue(true);
            confirm.Answers.Enqueue(true);
            Directory.CreateDirectory(h.Paths.ToolsDirectory);
            File.WriteAllText(Path.Combine(h.Paths.ToolsDirectory, "old.bin"), "x");
            (await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None)).Ready.ShouldBeTrue();
            Directory.GetDirectories(h.Paths.DataDirectory).Where(d => Path.GetFileName(d).StartsWith("tools.", StringComparison.Ordinal)).ShouldBeEmpty();
            used.Installs.ShouldBe(2);
        }
    }

    [AvaloniaFact]
    public async Task ADeclinedDownloadLeavesTheVersionUnrecordedSoTheQuestionComesBack()
    {
        var (h, used, _, dialog, _, confirm) = Setup(UpdateChoice.Update);
        using (h)
        {
            confirm.Answer = false;

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);
            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            used.Installs.ShouldBe(0);
            dialog.Asked.Count.ShouldBe(2, "nothing was updated: asked again at the next start");
            Preferences(h).Read().LastVersion.ShouldBeEmpty();
        }
    }

    [AvaloniaFact]
    public async Task WithoutInstalledComponentsTheVersionIsOnlyRecorded()
    {
        var (h, used, _, dialog, _, _) = Setup(UpdateChoice.Update);
        using (h)
        {
            Directory.Delete(used.Folder!, recursive: true);

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.ShouldBeEmpty();
            Preferences(h).Read().LastVersion.ShouldBe("0.1.16");
        }
    }

    [AvaloniaFact]
    public async Task AnEmptyFolderOrAPartialDownloadDoesNotCountAsInstalled()
    {
        var (h, used, _, dialog, _, _) = Setup(UpdateChoice.Update);
        using (h)
        {
            // An image model's folder is made before its download starts; the download failed and left only its part.
            File.Delete(Path.Combine(used.Folder!, "sd-cli.exe"));
            File.WriteAllText(Path.Combine(used.Folder!, "model.safetensors.part"), "half");
            Directory.CreateDirectory(Path.Combine(h.Paths.ToolsDirectory, "images", "empty"));

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.ShouldBeEmpty();
            ComponentFolders.HasContent(used.Folder!).ShouldBeFalse();
        }
    }

    [AvaloniaFact]
    public async Task EveryFinishedInstallIsRecordedWithTheAppVersionAndARemovalForgetsIt()
    {
        var (h, used, unused, _, _, _) = Setup(UpdateChoice.Update);
        using (h)
        {
            var planner = h.Get<ComponentPlanner>();
            var plan = await planner.PlanAsync([unused.Code], CancellationToken.None);

            await planner.InstallAsync(plan, null, CancellationToken.None);

            var record = h.Get<IInstallRecord>().List().ShouldHaveSingleItem();
            (record.Code, record.AppVersion).ShouldBe((unused.Code, TMConcept.Desktop.Logging.AppIdentity.ShortVersion));
            File.Exists(Path.Combine(h.Paths.DataDirectory, "installed-components.json")).ShouldBeTrue("outside the components folder");

            // One found on disk from before the record existed is recorded without a version.
            (await planner.InstalledAsync(CancellationToken.None)).ShouldBe([used.Code, unused.Code]);
            h.Get<IInstallRecord>().List().Single(r => r.Code == used.Code).AppVersion.ShouldBeEmpty();

            await planner.RemoveAsync(unused, h.Get<TMConcept.Desktop.DiskSpace.PermanentDiscard>(), CancellationToken.None);
            h.Get<IInstallRecord>().List().Select(r => r.Code).ShouldBe([used.Code]);
        }
    }

    [Fact]
    public void TheTitleNamesBothVersionsOrOnlyTheNewOne()
    {
        using var h = new DesktopHarness();
        var text = h.Get<Localizer>();
        text.Use("en");

        UpdateDialog.Title(text, "0.1.15", "0.1.16").ShouldBe("TM Concept was updated (0.1.15 → 0.1.16)");
        UpdateDialog.Title(text, string.Empty, "0.1.16").ShouldBe("TM Concept was updated (now 0.1.16)");
    }

    private static UiPreferencesFile Preferences(DesktopHarness h) => h.Get<UiPreferencesFile>();

    /// <summary>
    /// One component an earlier version installed (its folder is in the components folder, not ready now: a new pinned version) and one that
    /// was never installed; the dialog answers <paramref name="choice"/>, every download confirmation is accepted.
    /// </summary>
    private static (DesktopHarness H, FakeComponent Used, FakeComponent Unused, FakeUpdateDialog Dialog, FakeBin Bin, FakeConfirm Confirm) Setup(UpdateChoice choice)
    {
        var used = new FakeComponent("sd-cpp", ready: false);
        var unused = new FakeComponent("node", ready: false);
        var dialog = new FakeUpdateDialog(choice);
        var bin = new FakeBin();
        var confirm = new FakeConfirm();
        var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(confirm);
            s.AddSingleton<IUpdateDialog>(dialog);
            s.RemoveAll<IRecycleBin>();
            s.AddSingleton<IRecycleBin>(bin);
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(used, unused));
        });
        used.Folder = Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "master-old");
        Directory.CreateDirectory(used.Folder);
        File.WriteAllText(Path.Combine(used.Folder, "sd-cli.exe"), "old");
        return (h, used, unused, dialog, bin, confirm);
    }

    private sealed class FakeUpdateDialog(UpdateChoice choice) : IUpdateDialog
    {
        public List<(string From, string To)> Asked { get; } = [];

        public List<string> Folders { get; } = [];

        public Task<UpdateChoice> AskAsync(string from, string to, string toolsFolder)
        {
            Asked.Add((from, to));
            Folders.Add(toolsFolder);
            return Task.FromResult(choice);
        }
    }

    private sealed class FakeBin : IRecycleBin
    {
        public List<string> Moved { get; } = [];

        public bool Fail { get; set; }

        public Task MoveAsync(string folder, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new IOException("the recycle bin is not available");
            }

            Moved.Add(folder);
            Directory.Delete(folder, recursive: true);
            return Task.CompletedTask;
        }
    }
}

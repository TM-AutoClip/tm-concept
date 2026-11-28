using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// After a new app version (0.1.16): the installed components are updated, reinstalled from scratch or left for later; the version is recorded
/// in preferences.json only after an update or a reinstall that ended ready, or when nothing is installed. What was installed comes from the
/// install record (0.1.17, outside the components folder) and from folders that hold an install. 28 Nov 2026: every install deletes the old
/// files first, then downloads; what a run did not finish is resumed from the install journal at the next start. Fake components and dialog;
/// the delete is the app's own (inside the test's folder only): nothing is downloaded.
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
        var (h, used, unused, dialog, discard, confirm) = Setup(UpdateChoice.Update);
        using (h)
        {
            Preferences(h).Change(p => p.LastVersion = "0.1.15");

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.ShouldBe([("0.1.15", "0.1.16")]);
            dialog.Folders.Single().ShouldEndWith("tools");
            (used.Installs, unused.Installs).ShouldBe((1, 0), "only what was installed before, nothing else");
            discard.Discarded.ShouldBe([used.Folder!], "an update deletes only the old files of what it downloads again (28 Nov 2026)");
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
        var (h, used, _, dialog, discard, _) = Setup(UpdateChoice.Later);
        using (h)
        {
            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);
            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.ShouldBe([(string.Empty, "0.1.16"), (string.Empty, "0.1.16")], "coming from 0.1.15: no version was recorded, components are there");
            used.Installs.ShouldBe(0);
            discard.Discarded.ShouldBeEmpty();
            Preferences(h).Read().LastVersion.ShouldBeEmpty();
        }
    }

    /// <summary>
    /// 28 Nov 2026 (the developer: "sıfırdan kur ve güncellerde modelleri silecek önce, kalıcı tekte silecek, geri dönüşüm değil, sonra
    /// indirecek"): the whole components folder is deleted for good first, then what was installed is downloaded again; no old folder is set
    /// aside, no second copy is ever on the disk; the database stays.
    /// </summary>
    [AvaloniaFact]
    public async Task ReinstallDeletesTheWholeFolderForGoodFirstThenInstallsAgain()
    {
        var (h, used, unused, dialog, discard, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            File.WriteAllText(h.Paths.DatabaseFile + ".probe", "data");
            File.WriteAllText(h.Paths.KeyFile + ".probe", "key");
            Directory.CreateDirectory(h.Paths.BackupDirectory);
            File.WriteAllText(Path.Combine(h.Paths.BackupDirectory, "backup.probe"), "backup");
            used.OnInstall = () =>
            {
                // The install runs after the delete: the old files are gone, nothing was set aside, the journal names the component.
                discard.Discarded.ShouldBe([h.Paths.ToolsDirectory]);
                File.Exists(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "master-old", "sd-cli.exe")).ShouldBeFalse();
                Directory.GetFileSystemEntries(h.Paths.DataDirectory).Where(e => Path.GetFileName(e).StartsWith("tools.", StringComparison.Ordinal)).ShouldBeEmpty("no old folder next to tools");
                h.Get<IInstallJournal>().Unfinished().ShouldBe([used.Code]);
                Directory.CreateDirectory(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "new"));
                File.WriteAllText(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "new", "sd-cli.exe"), "new");
            };

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.Count.ShouldBe(1);
            dialog.Sizes.ShouldBe([3L], "the window shows what the folder holds (the old sd-cli.exe: 3 bytes), since it is deleted for good");
            confirm.Asked.Count.ShouldBe(1, "one confirmation: the downloads");
            File.Exists(Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "new", "sd-cli.exe")).ShouldBeTrue("the new install stays");
            (used.Installs, unused.Installs).ShouldBe((1, 0));
            h.Get<IInstallJournal>().Unfinished().ShouldBeEmpty("finished: nothing is resumed at the next start");
            File.Exists(h.Paths.DatabaseFile + ".probe").ShouldBeTrue("the data folder outside tools is never touched");
            File.Exists(h.Paths.KeyFile + ".probe").ShouldBeTrue();
            File.Exists(Path.Combine(h.Paths.BackupDirectory, "backup.probe")).ShouldBeTrue("the backups are never touched");
            Preferences(h).Read().LastVersion.ShouldBe("0.1.16");
        }
    }

    /// <summary>
    /// A declined download after the delete: the components are already gone, so they stay in the install journal and the next start offers
    /// them again (the start's resume); declined there too, they are installed when a step needs them.
    /// </summary>
    [AvaloniaFact]
    public async Task ADeclinedReinstallIsOfferedAgainAtTheNextStartAndThenForgotten()
    {
        var (h, used, _, _, _, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            confirm.Answer = false;

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            File.Exists(Path.Combine(used.Folder!, "sd-cli.exe")).ShouldBeFalse("deleted first, as the confirmation said");
            used.Installs.ShouldBe(0);
            h.Get<IInstallJournal>().Unfinished().ShouldBe([used.Code]);
            Preferences(h).Read().LastVersion.ShouldBeEmpty();

            // The next start: offered again, declined again → forgotten (installed when a step needs it).
            var outcome = await h.Get<TMConcept.Desktop.Components.InstallResume>().RunAsync(CancellationToken.None);
            outcome.ShouldNotBeNull().Declined.ShouldBeTrue();
            confirm.Messages[^1].ShouldContain(used.Name);
            h.Get<IInstallJournal>().Unfinished().ShouldBeEmpty();
            (await h.Get<TMConcept.Desktop.Components.InstallResume>().RunAsync(CancellationToken.None)).ShouldBeNull("nothing left to resume");
        }
    }

    [AvaloniaFact]
    public async Task ALibraryTheAppLoadedFromTheFolderStopsTheReinstallAndTheRemovalWithARestartHint()
    {
        var (h, used, _, _, discard, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            var marker = Path.Combine(used.Folder!, "sd-cli.exe");
            used.Ready = true;
            used.LoadedInProcess = true;
            var expected = h.Get<Localizer>().Format("components.loaded_restart", used.Name);

            var outcome = await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None);

            outcome.Ready.ShouldBeFalse();
            outcome.Reason.ShouldBe(expected);
            File.Exists(marker).ShouldBeTrue("nothing was deleted");
            h.Get<IInstallJournal>().Unfinished().ShouldBeEmpty();
            confirm.Asked.ShouldBeEmpty();
            used.Installs.ShouldBe(0);

            var cleaner = h.Get<TMConcept.Desktop.DiskSpace.IDiskCleaner>();
            var removal = await cleaner.RemoveAsync([cleaner.ItemOf(used)], CancellationToken.None);
            removal.Failed.ShouldHaveSingleItem().Reason.ShouldBe(expected);
            removal.Removed.ShouldBeEmpty();
            discard.Discarded.ShouldBeEmpty();
            confirm.Asked.ShouldBeEmpty("no offer to delete it for good either");
            File.Exists(marker).ShouldBeTrue();
        }
    }

    /// <summary>
    /// A reinstall that installed some components and not others (25 Nov 2026: 13 of 14, the yazgı-tts packages failed): the installed ones
    /// stay, the failed one stays in the journal for the next start, and no second copy of anything is left on the disk.
    /// </summary>
    [AvaloniaFact]
    public async Task APartlyFailedReinstallKeepsTheInstalledOnesAndLeavesTheFailedOneForTheNextStart()
    {
        var (h, used, other, _, _, _) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            other.Folder = Path.Combine(h.Paths.ToolsDirectory, "speech-py", "env-yazgi");
            Directory.CreateDirectory(other.Folder);
            File.WriteAllText(Path.Combine(other.Folder, "pyvenv.cfg"), "old");
            other.Fail = true;
            used.OnInstall = () =>
            {
                Directory.CreateDirectory(used.Folder!);
                File.WriteAllText(Path.Combine(used.Folder!, "sd-cli.exe"), "new");
            };

            var outcome = await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None);

            outcome.Ready.ShouldBeFalse("one component is still missing");
            outcome.Reason.ShouldEndWith(h.Get<Localizer>()["components.reinstall.resume"]);
            File.ReadAllText(Path.Combine(used.Folder!, "sd-cli.exe")).ShouldBe("new", "the new install stays");
            File.Exists(Path.Combine(other.Folder, "pyvenv.cfg")).ShouldBeFalse("its old files were deleted with the folder; nothing is carried back");
            Directory.GetFileSystemEntries(h.Paths.DataDirectory).Where(e => Path.GetFileName(e).StartsWith("tools.", StringComparison.Ordinal)).ShouldBeEmpty("no old or failed components folder next to tools");
            h.Get<IInstallJournal>().Unfinished().ShouldBe([other.Code], "the failed one is downloaded again at the next start");
        }
    }

    [AvaloniaFact]
    public async Task WhenTheFolderCannotBeDeletedNothingIsDownloadedAndTheNextStartGoesOn()
    {
        var (h, used, _, _, discard, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            discard.FailWhen = p => p == h.Paths.ToolsDirectory;

            var outcome = await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None);

            outcome.Ready.ShouldBeFalse();
            outcome.Reason.ShouldStartWith(h.Get<Localizer>().Format("components.reinstall.delete_failed", "x", "y")[..20]);
            confirm.Asked.ShouldBeEmpty("nothing is downloaded onto files that could not go");
            used.Installs.ShouldBe(0);
            h.Get<IInstallJournal>().Unfinished().ShouldBe([used.Code]);
            h.Get<TMConcept.Core.Activity.IActivityFeed>().Recent().ShouldContain(e => e.Level == TMConcept.Core.Activity.ActivityLevel.Error);
        }
    }

    /// <summary>An update (not from scratch): each component's old files go for good right before its own download, nothing else.</summary>
    [AvaloniaFact]
    public async Task AnUpdateDeletesEachComponentsOldFilesRightBeforeItsDownload()
    {
        var (h, used, unused, _, discard, _) = Setup(UpdateChoice.Update);
        using (h)
        {
            unused.Folder = Path.Combine(h.Paths.ToolsDirectory, "node", "22");
            used.OnInstall = () =>
            {
                File.Exists(Path.Combine(used.Folder!, "sd-cli.exe")).ShouldBeFalse("the old version is gone before the download");
                discard.Discarded.ShouldBe([used.Folder!]);
            };

            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            used.Installs.ShouldBe(1);
            discard.Discarded.ShouldBe([used.Folder!], "only the updated component's files");
            h.Get<IInstallJournal>().Unfinished().ShouldBeEmpty();
        }
    }

    /// <summary>
    /// The app closed in the middle of an install (the journal still names two components): at the next start the half-finished one's files
    /// are deleted for good and it is downloaded again; the one that was finished (ready) is kept and not downloaded again.
    /// </summary>
    [AvaloniaFact]
    public async Task AnInterruptedInstallDeletesOnlyTheHalfFinishedFilesAndDownloadsThemAgain()
    {
        var (h, used, unused, _, discard, confirm) = Setup(UpdateChoice.Update);
        using (h)
        {
            unused.Ready = true;
            unused.Folder = Path.Combine(h.Paths.ToolsDirectory, "node", "22");
            Directory.CreateDirectory(unused.Folder);
            File.WriteAllText(Path.Combine(unused.Folder, "node.exe"), "done");
            File.WriteAllText(Path.Combine(used.Folder!, "model.gguf.part"), "half");
            h.Get<IInstallJournal>().Begin([unused.Code, used.Code]);
            used.OnInstall = () => Directory.Exists(used.Folder!).ShouldBeFalse("the half-finished files (and the old ones) went first");

            var outcome = await h.Get<TMConcept.Desktop.Components.InstallResume>().RunAsync(CancellationToken.None);

            outcome.ShouldNotBeNull().Ready.ShouldBeTrue();
            (used.Installs, unused.Installs).ShouldBe((1, 0), "the finished one is not downloaded again");
            File.Exists(Path.Combine(unused.Folder, "node.exe")).ShouldBeTrue();
            discard.Discarded.ShouldBe([used.Folder!]);
            confirm.Messages.ShouldHaveSingleItem().ShouldNotContain(unused.Name);
            h.Get<IInstallJournal>().Unfinished().ShouldBeEmpty();
        }
    }

    /// <summary>
    /// The app closed while a dependency downloaded (a new stable-diffusion.cpp build): the journal names the dependency and the image model
    /// whose weights are complete, not ready only because of the dependency. At the next start only the dependency's files go; the weights
    /// stay and are not downloaded again (the confirmation never listed them).
    /// </summary>
    [AvaloniaFact]
    public async Task AnInterruptedInstallKeepsTheCompleteFilesOfAComponentThatWaitsOnlyForItsDependency()
    {
        var runtime = new FakeComponent("sd-cpp", ready: false);
        var model = new FakeComponent("image.flux", ready: true, runtime.Code) { ReadyOnlyWith = runtime };
        var discard = new RecordingDiscard();
        var confirm = new FakeConfirm();
        using var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(confirm);
            discard.Register(s);
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(runtime, model));
        });
        runtime.Folder = Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "master-new");
        model.Folder = Path.Combine(h.Paths.ToolsDirectory, "images", "flux");
        Directory.CreateDirectory(runtime.Folder);
        Directory.CreateDirectory(model.Folder);
        File.WriteAllText(Path.Combine(runtime.Folder, "sd-cli.exe.part"), "half");
        File.WriteAllText(Path.Combine(model.Folder, "flux.gguf"), "complete weights");
        model.Ready.ShouldBeFalse("its runtime is not ready");
        h.Get<IInstallJournal>().Begin([runtime.Code, model.Code]);

        var outcome = await h.Get<TMConcept.Desktop.Components.InstallResume>().RunAsync(CancellationToken.None);

        outcome.ShouldNotBeNull().Ready.ShouldBeTrue();
        (runtime.Installs, model.Installs).ShouldBe((1, 0));
        File.Exists(Path.Combine(model.Folder, "flux.gguf")).ShouldBeTrue();
        discard.Discarded.ShouldBe([runtime.Folder]);
        confirm.Messages.ShouldHaveSingleItem().ShouldNotContain(model.Name);
        h.Get<IInstallJournal>().Unfinished().ShouldBeEmpty();
    }

    /// <summary>
    /// What an install deletes before its download is only inside the components folder (the database and the key file are never reached,
    /// whatever a component names) and never another component's folder that lies inside it.
    /// </summary>
    [AvaloniaFact]
    public async Task AnInstallNeverDeletesTheDataFilesNorAnotherComponentsFolder()
    {
        var (h, used, unused, _, _, _) = Setup(UpdateChoice.Update);
        using (h)
        {
            foreach (var file in new[] { h.Paths.DatabaseFile, h.Paths.KeyFile })
            {
                if (!File.Exists(file))
                {
                    File.WriteAllText(file, "keep");
                }
            }

            var root = Path.Combine(h.Paths.ToolsDirectory, "sd-cpp");
            unused.Ready = true;
            unused.Folder = Path.Combine(root, "shared-weights");
            Directory.CreateDirectory(unused.Folder);
            File.WriteAllText(Path.Combine(unused.Folder, "w.bin"), "other");
            used.Replaced = [root, h.Paths.DatabaseFile, h.Paths.KeyFile, h.Paths.DataDirectory];
            var planner = h.Get<ComponentPlanner>();

            await planner.InstallAsync(await planner.PlanAsync([used.Code], CancellationToken.None), null, CancellationToken.None);

            used.Installs.ShouldBe(1);
            File.Exists(h.Paths.DatabaseFile).ShouldBeTrue();
            File.Exists(h.Paths.KeyFile).ShouldBeTrue();
            File.Exists(Path.Combine(unused.Folder, "w.bin")).ShouldBeTrue("another component's folder inside the replaced path stays");
            File.Exists(Path.Combine(used.Folder!, "sd-cli.exe")).ShouldBeFalse("everything else of it went");
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
    private static (DesktopHarness H, FakeComponent Used, FakeComponent Unused, FakeUpdateDialog Dialog, RecordingDiscard Discard, FakeConfirm Confirm) Setup(UpdateChoice choice)
    {
        var used = new FakeComponent("sd-cpp", ready: false);
        var unused = new FakeComponent("node", ready: false);
        var dialog = new FakeUpdateDialog(choice);
        var discard = new RecordingDiscard();
        var confirm = new FakeConfirm();
        var h = new DesktopHarness(s =>
        {
            s.AddSingleton<IConfirmDialog>(confirm);
            s.AddSingleton<IUpdateDialog>(dialog);
            discard.Register(s);
            s.RemoveAll<IComponentSource>();
            s.AddSingleton<IComponentSource>(new FakeComponents(used, unused));
        });
        used.Folder = Path.Combine(h.Paths.ToolsDirectory, "sd-cpp", "master-old");
        Directory.CreateDirectory(used.Folder);
        File.WriteAllText(Path.Combine(used.Folder, "sd-cli.exe"), "old");
        return (h, used, unused, dialog, discard, confirm);
    }

    private sealed class FakeUpdateDialog(UpdateChoice choice) : IUpdateDialog
    {
        public List<(string From, string To)> Asked { get; } = [];

        public List<string> Folders { get; } = [];

        public List<long> Sizes { get; } = [];

        public Task<UpdateChoice> AskAsync(string from, string to, string toolsFolder, long toolsBytes)
        {
            Asked.Add((from, to));
            Folders.Add(toolsFolder);
            Sizes.Add(toolsBytes);
            return Task.FromResult(choice);
        }
    }
}

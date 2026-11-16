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
/// in preferences.json only after an update, a reinstall or when nothing is installed. Fake components, dialog and recycle bin: nothing is
/// downloaded or moved to the real recycle bin.
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
    public async Task ReinstallMovesTheWholeComponentsFolderToTheRecycleBinThenInstallsWhatWasThere()
    {
        var (h, used, unused, dialog, bin, _) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            var database = h.Paths.DatabaseFile;
            await h.Get<VersionCheck>().RunAsync("0.1.16", CancellationToken.None);

            dialog.Asked.Count.ShouldBe(1);
            bin.Moved.ShouldBe([h.Paths.ToolsDirectory]);
            Directory.Exists(h.Paths.ToolsDirectory).ShouldBeFalse();
            (used.Installs, unused.Installs).ShouldBe((1, 0));
            Path.GetDirectoryName(database).ShouldBe(h.Paths.DataDirectory, "the database is outside the components folder");
            Preferences(h).Read().LastVersion.ShouldBe("0.1.16");
        }
    }

    [AvaloniaFact]
    public async Task WhenTheRecycleBinFailsAPermanentDeleteIsOnlyDoneAfterItsOwnConfirmation()
    {
        var (h, used, _, _, bin, confirm) = Setup(UpdateChoice.Reinstall);
        using (h)
        {
            bin.Fail = true;
            confirm.Answers.Enqueue(false);
            var text = h.Get<Localizer>();

            var outcome = await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None);

            outcome.ShouldBe(new InstallOutcome(false, text["components.reinstall.kept"]));
            confirm.Asked.ShouldBe([text["components.reinstall.bin_failed.title"]]);
            Directory.Exists(h.Paths.ToolsDirectory).ShouldBeTrue("declined: nothing was deleted");
            used.Installs.ShouldBe(0);

            // Confirmed: deleted for good, then installed again.
            confirm.Answers.Enqueue(true);
            (await h.Get<TMConcept.Desktop.Components.ComponentReinstaller>().ReinstallAsync(CancellationToken.None)).Ready.ShouldBeTrue();
            Directory.Exists(h.Paths.ToolsDirectory).ShouldBeFalse();
            used.Installs.ShouldBe(1);
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

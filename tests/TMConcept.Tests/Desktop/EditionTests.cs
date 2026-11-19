using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Desktop.Instance;
using TMConcept.Desktop.Updates;
using TMConcept.Desktop.ViewModels;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// Two editions (0.1.26, the developer): the installed one (Velopack Setup) and the portable program, the same data folder, one copy at a time.
/// The release's file names keep the portable updater on the portable program; an update keeps the program's own name; Settings → General
/// installs TM Concept from the portable program.
/// </summary>
public sealed class EditionTests
{
    /// <summary>Every file of the 0.1.26 release (tools/publish.ps1 writes them; the AppImage set only when it was built).</summary>
    private static readonly string[] Release0126 =
    [
        ReleaseAssetNames.PortableWindows,
        ReleaseAssetNames.PortableLinuxX64,
        ReleaseAssetNames.PortableLinuxArm64,
        ReleaseAssetNames.SetupWindows,
        "TMConcept-0.1.26-win-x64-full.nupkg",
        "TMConcept-0.1.27-win-x64-delta.nupkg",
        "releases.win-x64.json",
        "TMConcept-linux-x64.AppImage",
        "TMConcept-0.1.26-linux-x64-full.nupkg",
        "releases.linux-x64.json",
        "TMConcept-linux-arm64.AppImage",
        "TMConcept-0.1.26-linux-arm64-full.nupkg",
        "releases.linux-arm64.json",
    ];

    [Theory]
    [InlineData("win-x64", ReleaseAssetNames.PortableWindows)]
    [InlineData("linux-x64", ReleaseAssetNames.PortableLinuxX64)]
    [InlineData("linux-arm64", ReleaseAssetNames.PortableLinuxArm64)]
    public void ExactlyOneFilePerSystemMatchesThePortableUpdaterAndItIsThePortableOne(string rid, string portable)
    {
        // The portable updater's rule (ReleaseNotes.AssetFor): the first file ending with -<rid>.exe / -<rid>.tar.gz.
        var suffix = rid.StartsWith("win-", StringComparison.Ordinal) ? "-" + rid + ".exe" : "-" + rid + ".tar.gz";
        Release0126.Where(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ShouldBe([portable]);

        var release = new GitHubRelease { Tag = "v0.1.26", Assets = [.. Release0126.Select(n => new GitHubAsset { Name = n })] };
        ReleaseNotes.AssetFor(release, rid)!.Name.ShouldBe(portable);
    }

    [Fact]
    public void TheInstallerIsFoundByItsOwnSuffixAndNeverByThePortableRule()
    {
        var release = new GitHubRelease { Tag = "v0.1.26", Assets = [.. Release0126.Select(n => new GitHubAsset { Name = n })] };

        ReleaseNotes.SetupFor(release, "win-x64")!.Name.ShouldBe(ReleaseAssetNames.SetupWindows);
        ReleaseNotes.SetupFor(release, "linux-x64").ShouldBeNull();
        ReleaseAssetNames.SetupWindows.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Fact]
    public void ThePackingScriptsWriteTheSameNames()
    {
        var package = File.ReadAllText(Path.Combine(TMConcept.Tests.Architecture.RepositoryFiles.Root, "tools", "package.py"));
        var publish = File.ReadAllText(Path.Combine(TMConcept.Tests.Architecture.RepositoryFiles.Root, "tools", "publish.ps1"));

        package.ShouldContain("TMConcept-Portable-{rid}.exe");
        package.ShouldContain("tm-concept-portable-{rid}.tar.gz");
        ReleaseAssetNames.PortableWindows.ShouldBe("TMConcept-Portable-win-x64.exe");
        ReleaseAssetNames.PortableLinuxX64.ShouldBe("tm-concept-portable-linux-x64.tar.gz");
        ReleaseAssetNames.PortableLinuxArm64.ShouldBe("tm-concept-portable-linux-arm64.tar.gz");
        publish.ShouldContain("$packId = '" + ReleaseAssetNames.PackageId + "'");
        publish.ShouldContain("$packId-$channel" + ReleaseAssetNames.SetupSuffix);
        publish.ShouldContain("$aumid = '" + TMConcept.Desktop.Os.TaskbarIcon.AppId + "'");
        publish.ShouldContain("TMConcept-Portable-$rid.exe");
        publish.ShouldContain("tm-concept-portable-$rid.tar.gz");

        // The delta package's base is the full package the previous release really published (its SHA-256 block), never any local build.
        publish.ShouldContain("$repository = '" + UpdateRules.Repository["https://github.com/".Length..] + "'");
        publish.ShouldContain("its notes give no SHA-256 for it");
        publish.ShouldNotContain("-Filter 'release-*'", Case.Sensitive, "no newest-older-package search over local folders");
    }

    [Fact]
    public void TheNewVersionKeepsTheProgramsNameAndTheNextStartCleansUp()
    {
        var folder = Folder();
        var program = Path.Combine(folder, "My TM Concept.exe");
        File.WriteAllText(program, "old");
        var replacer = new SelfReplacer(NullLogger<SelfReplacer>.Instance) { ProgramPath = program };
        Directory.CreateDirectory(replacer.Staging);
        var download = Path.Combine(replacer.Staging, ReleaseAssetNames.PortableWindows);
        File.WriteAllText(download, "new");

        replacer.Replace(download).ShouldBe(program, "the program the user named keeps its name");
        File.ReadAllText(program).ShouldBe("new");

        // The old file (Windows keeps it as .old) is the way back until the main window opens.
        if (OperatingSystem.IsWindows())
        {
            replacer.PreviousProgram.ShouldBe(program + UpdateRules.OldSuffix);
        }

        replacer.CleanUp();
        replacer.PreviousProgram.ShouldBeNull();
        Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).ShouldBe(["My TM Concept.exe"]);
    }

    [Fact]
    public void TheEditionIsInstalledOnlyWhenSetupRecordedAVersionAndAFolder()
    {
        new VelopackEdition(null, false, null, null).Kind.ShouldBe(AppEditionKind.Portable);
        VelopackEdition.Current().Kind.ShouldBe(AppEditionKind.Portable, "a test process was not installed by Setup");
        new VelopackEdition("0.1.26", true, @"C:\x\TMConcept", null).Kind.ShouldBe(AppEditionKind.Portable, "Velopack's own portable zip is not built");
        new VelopackEdition("0.1.26", false, null, null).Kind.ShouldBe(AppEditionKind.Portable);

        var installed = new VelopackEdition("0.1.26", false, @"C:\Users\u\AppData\Local\TMConcept", "TMAutoClip.TMConcept");
        installed.Kind.ShouldBe(AppEditionKind.Installed);
        installed.InstalledVersion.ShouldBe("0.1.26");
        installed.InstallRoot.ShouldBe(@"C:\Users\u\AppData\Local\TMConcept");

        using var h = new DesktopHarness();
        h.Get<IAppEdition>().Kind.ShouldBe(AppEditionKind.Portable);
    }

    [Fact]
    public void TheDataFolderMustNotBeOrHoldOrLieInTheInstallFolder()
    {
        var local = Path.Combine(TestFolders.Root, "local");
        var data = Path.Combine(local, "TM Concept");
        var root = Path.Combine(local, "TMConcept");

        EditionGuard.Overlaps(data, root).ShouldBeFalse("the default folders: 'TM Concept' (data) and 'TMConcept' (install) side by side");
        EditionGuard.Overlaps(data, null).ShouldBeFalse();
        EditionGuard.Overlaps(data, data).ShouldBeTrue();
        EditionGuard.Overlaps(data, data + Path.DirectorySeparatorChar).ShouldBeTrue();
        EditionGuard.Overlaps(Path.Combine(root, "data"), root).ShouldBeTrue("inside the install folder");
        EditionGuard.Overlaps(local, root).ShouldBeTrue("the install folder inside the data folder");
        EditionGuard.Overlaps(root + "-old", root).ShouldBeFalse("only whole folder names count");
    }

    [Fact]
    public void BothEditionsShareTheDataFolderAndItsLockSoOnlyOneCopyRuns()
    {
        // The default data folder does not depend on the program's place or edition; the lock lives in it.
        var os = TMConcept.Desktop.Composition.DesktopServices.CurrentOs;
        os.DefaultDataDirectory().ShouldBe(os.DefaultDataDirectory());
        os.DefaultDataDirectory().ShouldNotContain("TMConcept" + Path.DirectorySeparatorChar + "current");

        var folder = Folder();
        using var installed = SingleInstance.Acquire(folder);
        using var portable = SingleInstance.Acquire(folder);
        installed.IsFirst.ShouldBeTrue();
        portable.IsFirst.ShouldBeFalse("the second copy (either edition) only asks the first to show its window");
    }

    [Fact]
    public void ANewerDatabaseOffersUpdatingThisCopyAndChangesNothing()
    {
        using var h = new DesktopHarness();
        var unlock = h.Get<UnlockViewModel>();

        unlock.Show(new DatabaseVersionException("x") { Problem = DatabaseVersionProblem.Newer, Step = "Version0126" });
        unlock.CanUpdateCopy.ShouldBeTrue();
        unlock.CanSetAside.ShouldBeFalse("a newer database is never set aside by an older copy");

        unlock.Show(new DatabaseVersionException("x") { Problem = DatabaseVersionProblem.Unrecorded });
        unlock.CanUpdateCopy.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdatingThisCopyWithoutANewerVersionSaysWhereToGetOne()
    {
        using var h = new DesktopHarness();
        var unlock = h.Get<UnlockViewModel>();
        unlock.Show(new DatabaseVersionException("x") { Problem = DatabaseVersionProblem.Newer, Step = "Version0126" });

        await unlock.UpdateCopyCommand.ExecuteAsync(null);

        unlock.Error.ShouldBe(h.Get<TMConcept.Desktop.Localization.Localizer>()["database.update_none"], "offline in the test: nothing is found");
        unlock.Busy.ShouldBeFalse();
    }

    [Fact]
    public void TheInstalledEditionIsNeverOfferedTheInstall()
    {
        using var installed = new DesktopHarness(s =>
            s.AddSingleton<IAppEdition>(new VelopackEdition("0.1.26", false, Folder(), TMConcept.Desktop.Os.TaskbarIcon.AppId)));
        Installer(installed).Available.ShouldBeFalse("the installed edition is already installed");
    }

    [Fact]
    public async Task AnInstalledEditionAlreadyOnThisComputerIsNeverInstalledOver()
    {
        var program = Path.Combine(Folder(), ReleaseAssetNames.PortableWindows);
        File.WriteAllText(program, "the portable program");
        using var h = new DesktopHarness(s => s.AddSingleton(new SelfReplacer(NullLogger<SelfReplacer>.Instance) { ProgramPath = program }));
        var root = h.Get<InstalledCopy>().Root;
        h.Get<InstalledCopy>().Version().ShouldBeNull("nothing installed yet");

        // Installed with Setup from the browser (0.1.27); later this portable 0.1.26 is started.
        Directory.CreateDirectory(Path.Combine(root, "current"));
        File.WriteAllText(Path.Combine(root, "Update.exe"), "Velopack");
        File.WriteAllText(Path.Combine(root, "current", "sq.version"),
            "<?xml version=\"1.0\"?><package xmlns=\"http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd\"><metadata><id>TMConcept</id><version>0.1.27</version></metadata></package>");
        var installer = Installer(h, current: "0.1.26");

        installer.InstalledVersion.ShouldBe("0.1.27");
        (await Should.ThrowAsync<AppUpdateException>(() => installer.InstallAsync(null, CancellationToken.None))).Code.ShouldBe("already_installed",
            "an older Setup is never run over a newer install");

        // The same version: nothing to install either; an unreadable record counts as installed.
        File.WriteAllText(Path.Combine(root, "current", "sq.version"), "<package><metadata><version>0.1.26</version></metadata></package>");
        (await Should.ThrowAsync<AppUpdateException>(() => installer.InstallAsync(null, CancellationToken.None))).Code.ShouldBe("already_installed");
        File.WriteAllText(Path.Combine(root, "current", "sq.version"), "<damaged");
        installer.InstalledVersion.ShouldBe("unknown");

        // An older install is updated by this version's Setup (the check goes on to the release: offline in the test).
        File.WriteAllText(Path.Combine(root, "current", "sq.version"), "<package><metadata><version>0.1.25</version></metadata></package>");
        string? code = null;
        try
        {
            await installer.InstallAsync(null, CancellationToken.None);
        }
        catch (AppUpdateException ex)
        {
            code = ex.Code;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TMConcept.Core.Tools.ToolInstallException)
        {
            code = "offline";
        }

        code.ShouldNotBeNull().ShouldNotBe("already_installed");
    }

    private static EditionInstaller Installer(DesktopHarness h, string? current = null) =>
        new(h.Get<ReleaseFeed>(), h.Get<TMConcept.Engines.Tools.ToolDownloader>(), h.Get<SelfReplacer>(), h.Get<IAppEdition>(), h.Get<ISetupLauncher>(),
            h.Get<IAppRestarter>(), h.Get<TMConcept.Desktop.Startup.AppSession>(), h.Get<InstalledCopy>(), NullLogger<EditionInstaller>.Instance)
        {
            Rid = "win-x64",
            Current = current ?? TMConcept.Desktop.Logging.AppIdentity.ShortVersion,
        };

    private static string Folder()
    {
        var folder = Path.Combine(TestFolders.Root, "edition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}

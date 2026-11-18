using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Updates;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Engines;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The app's own update (the developer, 16 Nov 2026): GitHub's latest release is read at most once a day (ETag, rate limit respected),
/// compared by semantic version, offered with its notes, and installed only after a confirmation with the SHA-256 from the release notes
/// (missing or different = refused); the running program is replaced and started again. A fake GitHub and a fake download server: no test
/// reads GitHub.
/// </summary>
public sealed class AppUpdateTests
{
    private static readonly byte[] NewProgram = Encoding.UTF8.GetBytes("the new TM Concept program");

    [Theory]
    [InlineData("0.1.18", "0.1.17", true)]
    [InlineData("v0.2.0", "0.1.99", true)]
    [InlineData("0.1.17", "0.1.17", false)]
    [InlineData("0.1.17", "0.1.17+abc", false)]
    [InlineData("0.1.16", "0.1.17", false)]
    [InlineData("0.1.10", "0.1.9", true)]
    [InlineData("0.2.0-beta.2", "0.2.0-beta.10", false)]
    [InlineData("0.2.0", "0.2.0-rc.1", true)]
    [InlineData("0.2.0-rc.1", "0.1.17", true)]
    public void VersionsCompareAsSemanticVersions(string latest, string current, bool newer)
    {
        AppVersion.TryParse(latest, out var a).ShouldBeTrue();
        AppVersion.TryParse(current, out var b).ShouldBeTrue();
        a.IsNewerThan(b).ShouldBe(newer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("0.1.x")]
    [InlineData("0.1.2-")]
    public void ANameThatIsNotAVersionIsRejected(string text) => AppVersion.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void TheSha256OfAFileIsReadFromTheReleaseNotes()
    {
        var hash = new string('a', 64);
        var notes = $"""
            ## Changes
            - Something new.

            ## SHA-256
            ```
            {hash}  TMConcept-0.1.18-win-x64.exe  (60.1 MiB)
            {new string('b', 64)}  tm-concept-0.1.18-linux-x64.tar.gz  (58.0 MiB)
            ```
            """;

        ReleaseNotes.Sha256For(notes, "TMConcept-0.1.18-win-x64.exe").ShouldBe(hash);
        ReleaseNotes.Sha256For(notes, "tm-concept-0.1.18-linux-x64.tar.gz").ShouldBe(new string('b', 64));
        ReleaseNotes.Sha256For(notes, "tm-concept-0.1.18-linux-arm64.tar.gz").ShouldBeNull("no line names it");
        ReleaseNotes.Sha256For(notes.Replace("SHA-256", "Checks", StringComparison.Ordinal), "TMConcept-0.1.18-win-x64.exe").ShouldBeNull("no SHA-256 block");
        ReleaseNotes.Sha256For(notes + $"\n{new string('c', 64)}  TMConcept-0.1.18-win-x64.exe", "TMConcept-0.1.18-win-x64.exe").ShouldBeNull("two different values");
        ReleaseNotes.Sha256For($"SHA-256: {hash}  TMConcept-0.1.18-win-x64.exe.sig", "TMConcept-0.1.18-win-x64.exe").ShouldBeNull("another file");
        ReleaseNotes.Sha256For($"| TMConcept-0.1.18-win-x64.exe | `{hash.ToUpperInvariant()}` | (SHA-256)", "TMConcept-0.1.18-win-x64.exe").ShouldBe(hash);
    }

    [Fact]
    public async Task GitHubIsAskedAtMostOnceADayWithItsETagAndAForcedLookCostsNothingWhenUnchanged()
    {
        var github = new FakeGitHub(Release("v0.1.18", "notes"));
        using var h = Harness(github, out _, out var clock);
        var feed = h.Get<ReleaseFeed>();

        (await feed.LatestAsync(false, CancellationToken.None))!.Tag.ShouldBe("v0.1.18");
        (await feed.LatestAsync(false, CancellationToken.None))!.Tag.ShouldBe("v0.1.18");
        github.Requests.Count.ShouldBe(1, "the second look within a day is the kept answer");
        github.Requests[0].Headers.UserAgent.ToString().ShouldBe(UpdateRules.UserAgent);
        github.Requests[0].Headers.Authorization.ShouldBeNull("no token");
        github.Requests[0].RequestUri.ShouldBe(UpdateRules.Releases);

        (await feed.LatestAsync(true, CancellationToken.None))!.Tag.ShouldBe("v0.1.18");
        github.Requests.Count.ShouldBe(2);
        github.Requests[1].Headers.IfNoneMatch.ToString().ShouldBe("\"r1\"", "an unchanged release costs nothing of the hourly limit (304)");

        clock.Advance(TimeSpan.FromDays(1.1));
        github.Releases = [Release("v0.1.19", "newer"), Release("v0.1.18", "notes")];
        (await feed.LatestAsync(false, CancellationToken.None))!.Tag.ShouldBe("v0.1.19");
    }

    [Fact]
    public async Task TheRateLimitIsRespectedUntilGitHubsResetTime()
    {
        var github = new FakeGitHub(Release("v0.1.18", "notes"));
        using var h = Harness(github, out _, out var clock);
        var feed = h.Get<ReleaseFeed>();
        github.Limited = clock.GetUtcNow().AddMinutes(30);

        (await feed.LatestAsync(true, CancellationToken.None)).ShouldBeNull();
        (await feed.LatestAsync(true, CancellationToken.None)).ShouldBeNull();
        github.Requests.Count.ShouldBe(1, "not even Check now asks before the reset time");

        clock.Advance(TimeSpan.FromMinutes(31));
        github.Limited = null;
        (await feed.LatestAsync(true, CancellationToken.None))!.Tag.ShouldBe("v0.1.18");
    }

    [Fact]
    public async Task OfflineTheLastKnownReleaseIsUsed()
    {
        using var h = new DesktopHarness();

        (await h.Get<ReleaseFeed>().LatestAsync(true, CancellationToken.None)).ShouldBeNull();
        (await h.Get<AppUpdater>().CheckAsync(true, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ANewerReleaseIsOfferedWithItsNotesAndTheSha256ForThisComputer()
    {
        using var h = Harness(new FakeGitHub(Release("v0.1.18", Notes(NewProgram))), out _, out _);

        var offer = (await Updater(h).CheckAsync(false, CancellationToken.None)).ShouldNotBeNull();

        offer.Version.ToString().ShouldBe("0.1.18");
        offer.Current.ShouldBe("0.1.17");
        offer.Notes.ShouldContain("Faster images");
        offer.Asset!.Name.ShouldBe("TMConcept-0.1.18-win-x64.exe");
        offer.Sha256.ShouldBe(Hash(NewProgram));
        offer.CanInstall.ShouldBeTrue();
    }

    [Fact]
    public async Task TheSameOrAnOlderVersionIsNotOffered()
    {
        using var h = Harness(new FakeGitHub(Release("v0.1.17", Notes(NewProgram))), out _, out _);

        (await Updater(h).CheckAsync(true, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task WithoutASha256InTheNotesOrWithADifferentGitHubDigestTheReleaseIsNotInstallable()
    {
        using var h = Harness(new FakeGitHub(Release("v0.1.18", "Only text, no checksums.")), out _, out _);
        var offer = (await Updater(h).CheckAsync(true, CancellationToken.None)).ShouldNotBeNull();
        offer.Problem.ShouldBe("no_hash");
        offer.CanInstall.ShouldBeFalse();
        await Should.ThrowAsync<AppUpdateException>(() => Updater(h).InstallAsync(offer, null, CancellationToken.None));

        var release = Release("v0.1.18", Notes(NewProgram));
        release.Assets[0].Digest = "sha256:" + new string('0', 64);
        using var other = Harness(new FakeGitHub(release), out _, out _);
        (await Updater(other).CheckAsync(true, CancellationToken.None))!.Problem.ShouldBe("no_hash");
    }

    [Fact]
    public async Task ABuildFromSourceDoesNotReplaceItself()
    {
        using var h = Harness(new FakeGitHub(Release("v0.1.18", Notes(NewProgram))), out var program, out _);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(program)!, "TMConcept.dll"), "a build from source has its assembly next to it");

        (await Updater(h).CheckAsync(true, CancellationToken.None))!.Problem.ShouldBe("not_packaged");
    }

    [Fact]
    public async Task AConfirmedUpdateIsDownloadedCheckedSwappedAndStartedAgain()
    {
        var restarter = new FakeRestarter();
        using var h = Harness(new FakeGitHub(Release("v0.1.18", Notes(NewProgram))), out var program, out _, restarter, new FlakyServer(NewProgram));
        var updater = Updater(h);
        var offer = (await updater.CheckAsync(true, CancellationToken.None))!;

        await updater.InstallAsync(offer, null, CancellationToken.None);

        // 0.1.26: the program was named by its release (TMConcept-0.1.17-win-x64.exe); the new version gets the versionless portable name.
        var portable = Path.Combine(Path.GetDirectoryName(program)!, ReleaseAssetNames.PortableWindows);
        File.ReadAllBytes(portable).ShouldBe(NewProgram, "the new version keeps no misleading version in its name");
        if (OperatingSystem.IsWindows())
        {
            File.ReadAllText(program + UpdateRules.OldSuffix).ShouldBe("the old program", "the running file was renamed aside, not overwritten");
        }
        else
        {
            File.Exists(program).ShouldBeFalse();
        }

        restarter.Started.ShouldBe([portable]);

        // The next start removes what the update left.
        h.Get<SelfReplacer>().CleanUp();
        File.Exists(program + UpdateRules.OldSuffix).ShouldBeFalse();
        Directory.Exists(h.Get<SelfReplacer>().Staging).ShouldBeFalse();
    }

    [Fact]
    public async Task AFileThatDoesNotMatchTheNotesIsRefusedAndNothingIsReplaced()
    {
        var restarter = new FakeRestarter();
        var tampered = Encoding.UTF8.GetBytes("something else");
        using var h = Harness(new FakeGitHub(Release("v0.1.18", Notes(NewProgram))), out var program, out _, restarter, new FlakyServer(tampered));
        var updater = Updater(h);
        var offer = (await updater.CheckAsync(true, CancellationToken.None))!;

        var error = await Should.ThrowAsync<ToolInstallException>(() => updater.InstallAsync(offer, null, CancellationToken.None));

        error.Code.ShouldBe("hash");
        File.ReadAllText(program).ShouldBe("the old program");
        restarter.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnInstalledUpdateWhoseProgramCannotStartSaysSoAndKeepsThePreviousProgram()
    {
        var restarter = new FakeRestarter { Fail = true };
        using var h = Harness(new FakeGitHub(Release("v0.1.18", Notes(NewProgram))), out var program, out _, restarter, new FlakyServer(NewProgram));
        var updater = Updater(h);
        var offer = (await updater.CheckAsync(true, CancellationToken.None))!;

        var error = await Should.ThrowAsync<AppUpdateException>(() => updater.InstallAsync(offer, null, CancellationToken.None));

        error.Code.ShouldBe("restart", "installed, but not started: the user starts the app again (not a generic crash text)");
        File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(program)!, ReleaseAssetNames.PortableWindows)).ShouldBe(NewProgram);
        if (OperatingSystem.IsWindows())
        {
            h.Get<SelfReplacer>().PreviousProgram.ShouldBe(program + UpdateRules.OldSuffix, "kept until the new version has started");
        }
    }

    [Fact]
    public async Task NothingIsReplacedWhileWorkRuns()
    {
        var restarter = new FakeRestarter();
        using var h = Harness(new FakeGitHub(Release("v0.1.18", Notes(NewProgram))), out var program, out _, restarter, new FlakyServer(NewProgram));
        h.Get<TMConcept.Desktop.Startup.AppSession>().WorkRunning = true;
        var updater = Updater(h);
        var offer = (await updater.CheckAsync(true, CancellationToken.None))!;

        (await Should.ThrowAsync<AppUpdateException>(() => updater.InstallAsync(offer, null, CancellationToken.None))).Code.ShouldBe("busy");
        File.ReadAllText(program).ShouldBe("the old program");
        restarter.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheStartOffersAVersionOnceADayAndNotAtAllWhenTurnedOff()
    {
        using var h = Harness(new FakeGitHub(Release("v0.1.18", Notes(NewProgram))), out _, out var clock);
        var updater = Updater(h);

        (await updater.CheckAtStartAsync(CancellationToken.None)).ShouldNotBeNull();
        (await updater.CheckAtStartAsync(CancellationToken.None)).ShouldBeNull("Later: not shown again the same day");
        clock.Advance(TimeSpan.FromDays(1.1));
        (await updater.CheckAtStartAsync(CancellationToken.None)).ShouldNotBeNull();

        h.Get<UiPreferencesFile>().Change(p => p.CheckForUpdates = false);
        clock.Advance(TimeSpan.FromDays(2));
        (await updater.CheckAtStartAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void ALinuxReleaseArchiveIsUnpackedAndOneThatLeavesItsFolderIsRefused()
    {
        using var paths = new TMConcept.Tests.Storage.TempPaths();
        var program = Path.Combine(paths.DataDirectory, "tm-concept", "tm-concept");
        Directory.CreateDirectory(Path.GetDirectoryName(program)!);
        File.WriteAllText(program, "old");
        var replacer = new SelfReplacer(NullLogger<SelfReplacer>.Instance) { ProgramPath = program };
        Directory.CreateDirectory(replacer.Staging);

        var good = Path.Combine(replacer.Staging, "tm-concept-0.1.18-linux-x64.tar.gz");
        File.WriteAllBytes(good, TarGz(("tm-concept/tm-concept", NewProgram), ("tm-concept/README.md", "readme"u8.ToArray())));
        var unpacked = replacer.Unpack(good);
        File.ReadAllBytes(unpacked).ShouldBe(NewProgram);

        var evil = Path.Combine(replacer.Staging, "evil.tar.gz");
        File.WriteAllBytes(evil, TarGz(("tm-concept/../../outside", NewProgram)));
        Should.Throw<InvalidDataException>(() => replacer.Unpack(evil));
    }

    [Fact]
    public void TheRestartedAppWaitsForTheOldProcess()
    {
        var options = StartOptions.Parse([UpdateRules.AfterUpdateArgument, "4242", "--data-dir", "x"]);
        options.WaitForProcess.ShouldBe(4242);
        options.DataDirectory.ShouldBe(Path.GetFullPath("x"));
        StartOptions.Parse([]).WaitForProcess.ShouldBeNull();
    }

    [Fact]
    public async Task EveryNewerPublishedVersionIsOfferedOldestFirstAndOnlyTheNewestIsInstalled()
    {
        var newest = Release("v0.1.20", Notes(NewProgram).Replace("0.1.18", "0.1.20", StringComparison.Ordinal));
        var draft = Release("v0.1.21", "draft");
        draft.Draft = true;
        var candidate = Release("v0.1.22-rc.1", "pre-release");
        candidate.Prerelease = true;
        var github = new FakeGitHub(candidate, draft, newest, Release("v0.1.19", "## English\n- Nineteen."), Release("v0.1.18", "## English\n- Eighteen."),
            Release("v0.1.17", "running"), Release("v0.1.16", "older"));
        using var h = Harness(github, out _, out _);

        var offer = (await Updater(h).CheckAsync(true, CancellationToken.None)).ShouldNotBeNull();

        offer.Version.ToString().ShouldBe("0.1.20", "drafts and pre-releases are never offered");
        offer.Versions.Select(v => v.Version.ToString()).ShouldBe(["0.1.18", "0.1.19", "0.1.20"], "every version since the running one, oldest first");
        offer.Asset!.Name.ShouldBe("TMConcept-0.1.20-win-x64.exe", "only the newest file is downloaded");
        offer.Sha256.ShouldBe(Hash(NewProgram), "the newest file is still checked against its notes");
        github.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheReleaseListIsReadPageByPageAndOnlyTheFirstPageCarriesTheETag()
    {
        var github = new FakeGitHub(Release("v0.1.22", "a"), Release("v0.1.21", "b"), Release("v0.1.20", "c"), Release("v0.1.19", "d"), Release("v0.1.18", "e"))
        {
            PageSize = 2,
        };
        using var h = Harness(github, out _, out var clock);
        var feed = h.Get<ReleaseFeed>();

        (await feed.ReleasesAsync(true, CancellationToken.None)).Select(r => r.Tag).ShouldBe(["v0.1.22", "v0.1.21", "v0.1.20", "v0.1.19", "v0.1.18"]);
        github.Requests.Count.ShouldBe(3, "five releases in pages of two");
        github.Requests[1].RequestUri!.Query.ShouldContain("page=2", Case.Sensitive, "the Link header's next page is followed");

        // The next look sends the first page's ETag; unchanged, it is one request and the kept list.
        (await feed.ReleasesAsync(true, CancellationToken.None)).Count.ShouldBe(5);
        github.Requests.Count.ShouldBe(4);
        github.Requests[3].Headers.IfNoneMatch.ToString().ShouldBe("\"r-v0.1.22\"");

        // Within a day the kept list is used without asking.
        (await feed.ReleasesAsync(false, CancellationToken.None)).Count.ShouldBe(5);
        github.Requests.Count.ShouldBe(4);
        clock.Advance(TimeSpan.FromDays(1.1));
        github.Releases.Insert(0, Release("v0.1.23", "f"));
        (await feed.ReleasesAsync(false, CancellationToken.None))[0].Tag.ShouldBe("v0.1.23");
    }

    [Fact]
    public async Task AListReadOnlyInPartIsKeptWithoutAnETagAndAskedAgainAfterTheLimit()
    {
        var github = new FakeGitHub(Release("v0.1.20", "a"), Release("v0.1.19", "b"), Release("v0.1.18", "c")) { PageSize = 2 };
        using var h = Harness(github, out _, out var clock);
        var feed = h.Get<ReleaseFeed>();
        github.Limited = clock.GetUtcNow().AddMinutes(30);
        github.LimitedFromPage = 2;

        (await feed.ReleasesAsync(true, CancellationToken.None)).Select(r => r.Tag).ShouldBe(["v0.1.20", "v0.1.19"], "the pages read so far are used");
        feed.State().ETag.ShouldBeNull("a partial list is never confirmed by a 304");
        (await feed.ReleasesAsync(true, CancellationToken.None)).Count.ShouldBe(2);
        github.Requests.Count.ShouldBe(2, "not asked again before GitHub's reset time");

        clock.Advance(TimeSpan.FromMinutes(31));
        github.Limited = null;
        (await feed.ReleasesAsync(true, CancellationToken.None)).Count.ShouldBe(3);
        github.Requests[2].Headers.IfNoneMatch.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACheckFileFrom0117WithOnlyTheLatestReleaseIsLookedAtAgainAtOnce()
    {
        var github = new FakeGitHub(Release("v0.1.19", "b"), Release("v0.1.18", "a"));
        using var h = Harness(github, out _, out var clock);
        var at = clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(h.Paths.UpdateCheckFile, "{ \"CheckedAtUtc\": \"" + at + "\", \"ETag\": \"\\\"r1\\\"\", \"Release\": { \"tag_name\": \"v0.1.18\" } }");

        (await h.Get<ReleaseFeed>().ReleasesAsync(false, CancellationToken.None)).Count.ShouldBe(2);
        github.Requests.ShouldHaveSingleItem().Headers.IfNoneMatch.ShouldBeEmpty("the old file has no list to confirm");
    }

    [Fact]
    public void TheNotesShowOnlyTheAppsLanguageWithoutTheSha256Block()
    {
        var hash = new string('a', 64);
        var notes = string.Join('\n',
            "# TM Concept 0.1.19",
            string.Empty,
            "[English](#english) · [Türkçe](#türkçe)",
            string.Empty,
            "## English",
            string.Empty,
            "### What changed since 0.1.18",
            string.Empty,
            "**Storage**",
            "- A new **Storage** tab in `Settings`: see what takes",
            "  space and [remove it](https://example.org).",
            "  - Old voices go to the recycle bin.",
            string.Empty,
            "## Türkçe",
            string.Empty,
            "### 0.1.18'den bu yana değişenler",
            string.Empty,
            "**Depolama**",
            "- Ayarlar'da yeni **Depolama** sekmesi.",
            string.Empty,
            "## SHA-256",
            string.Empty,
            "```",
            hash + "  TMConcept-0.1.19-win-x64.exe  (60.1 MiB)",
            "```");

        ReleaseNotesText.For(notes, "tr").ShouldBe("0.1.18'den bu yana değişenler\n\nDepolama\n• Ayarlar'da yeni Depolama sekmesi.");
        ReleaseNotesText.For(notes, "en").ShouldBe(
            "What changed since 0.1.18\n\nStorage\n• A new Storage tab in Settings: see what takes space and remove it.\n  • Old voices go to the recycle bin.");
        ReleaseNotesText.For(notes, "de").ShouldStartWith("What changed", Case.Sensitive, "another language falls back to English");
        ReleaseNotesText.For(notes.Replace("## Türkçe", "## Notes", StringComparison.Ordinal), "tr").ShouldStartWith("What changed", Case.Sensitive,
            "no Turkish section: English");
        ReleaseNotesText.For(notes, "tr").ShouldNotContain(hash);
        ReleaseNotesText.For(notes, "en").ShouldNotContain("SHA-256");

        // Notes without language sections are shown whole, still without the SHA-256 block (also when it is a lower heading).
        var plain = "## Changes\n- Faster images.\n\n### SHA-256\n" + hash + "  TMConcept-0.1.19-win-x64.exe\n\n## Thanks\n- Everyone.";
        ReleaseNotesText.For(plain, "tr").ShouldBe("Changes\n• Faster images.\n\nThanks\n• Everyone.");
        ReleaseNotesText.For(null, "en").ShouldBeEmpty();
    }

    [Fact]
    public void TheRealReleaseNotesOf0117ReadInBothLanguages()
    {
        var file = Path.Combine(TMConcept.Tests.Architecture.RepositoryFiles.Root, "docs", "release-notes", "v0.1.17.md");
        var body = File.ReadAllText(file) + "\n## SHA-256\n\n" + new string('b', 64) + "  TMConcept-0.1.17-win-x64.exe  (60.1 MiB)\n";

        var turkish = ReleaseNotesText.For(body, "tr");
        var english = ReleaseNotesText.For(body, "en");

        turkish.ShouldStartWith("0.1.16'dan bu yana değişenler");
        turkish.ShouldNotContain("What changed");
        english.ShouldStartWith("What changed since 0.1.16");
        english.ShouldNotContain("değişenler");
        (turkish + english).ShouldNotContain("**");
        (turkish + english).ShouldNotContain(new string('b', 64));
    }

    [Fact]
    public void TheWindowListsEveryVersionInTheAppsLanguage()
    {
        using var h = new DesktopHarness();
        var text = h.Get<TMConcept.Desktop.Localization.Localizer>();
        text.Use("tr");
        var offer = new UpdateOffer(new AppVersion(0, 1, 20, string.Empty), "0.1.17", "## English\n- Twenty.\n\n## Türkçe\n- Yirmi.", UpdateRules.ReleasesPage, null,
            null, "no_asset")
        {
            Versions =
            [
                new VersionNotes(new AppVersion(0, 1, 19, string.Empty), "## English\n- Nineteen.\n\n## Türkçe\n- On dokuz.", null),
                new VersionNotes(new AppVersion(0, 1, 20, string.Empty), "## English\n- Twenty.\n\n## Türkçe\n- Yirmi.", null),
            ],
        };

        using var model = new TMConcept.Desktop.Dialogs.AppUpdateViewModel(offer, h.Get<AppUpdater>(), h.Get<TMConcept.Desktop.Os.IShellLauncher>(), text,
            NullLogger.Instance);

        model.Title.ShouldBe("Yeni güncelleme var");
        model.Subtitle.ShouldContain("0.1.17");
        model.Versions.Select(v => (v.Header, v.Notes)).ShouldBe([("0.1.19", "• On dokuz."), ("0.1.20", "• Yirmi.")]);
    }

    [AvaloniaFact]
    public void TheRestartClosesTheWindowsOnTheUiThreadEvenWhenTheInstallEndsOnAPoolThread()
    {
        // 0.1.23 (the developer's log, 18 Nov 2026): the install continued on a pool thread after its awaits and the restarter closed the app
        // there; Avalonia threw "a different thread owns it" and the update was reported as not started although the new version ran. The
        // headless test dispatcher lets every thread in, so the thread check and the post are the test's: off the UI thread the close waits
        // for the UI thread's queue.
        var window = new Window();
        window.Show();
        var dialog = new Window();
        dialog.Show(window);
        Dispatcher.UIThread.RunJobs();
        var queue = new List<Action>();

        AppRestarter.OnUiThread(window.Close, () => false, queue.Add);

        window.IsVisible.ShouldBeTrue("not closed on the calling thread");
        queue.ShouldHaveSingleItem().ShouldBe(window.Close);
        Dispatcher.UIThread.Invoke(queue[0]);
        window.IsVisible.ShouldBeFalse();
        dialog.IsVisible.ShouldBeFalse();

        // On the UI thread: at once, nothing posted.
        var now = false;
        AppRestarter.OnUiThread(() => now = true, () => true, queue.Add);
        now.ShouldBeTrue();
        queue.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheInstalledEditionGetsVelopacksPackagesOnlyWhenTheNotesConfirmEveryOne()
    {
        var full = new InstalledUpdateFile("TMConcept-0.1.18-win-x64-full.nupkg", "0.1.18", Hash(NewProgram), 1000);
        var delta = new InstalledUpdateFile("TMConcept-0.1.18-win-x64-delta.nupkg", "0.1.18", new string('d', 64), 10);
        var release = Release("v0.1.18", Notes(NewProgram) + $"\n{full.Sha256}  {full.FileName}\n{delta.Sha256}  {delta.FileName}\n");
        release.Assets.Add(Asset("v0.1.18", full.FileName, full.Size));
        release.Assets.Add(Asset("v0.1.18", delta.FileName, delta.Size));
        var packages = new FakeInstalledUpdates { Update = new InstalledUpdate("0.1.18", full, [delta]) };
        var restarter = new FakeRestarter();
        using var h = Harness(new FakeGitHub(release), out var program, out _, restarter, more: s =>
        {
            s.AddSingleton<IAppEdition>(new VelopackEdition("0.1.17", false, Path.Combine(Path.GetTempPath(), "tmconcept-tests", "installed"), TMConcept.Desktop.Os.TaskbarIcon.AppId));
            s.AddSingleton<IInstalledUpdates>(packages);
        });
        var updater = Updater(h);

        var offer = (await updater.CheckAsync(true, CancellationToken.None)).ShouldNotBeNull();
        offer.CanInstall.ShouldBeTrue();
        offer.Installed.ShouldBe(packages.Update);
        offer.Asset!.Size.ShouldBe(10, "only the delta is downloaded");

        await updater.InstallAsync(offer, null, CancellationToken.None);
        packages.Downloaded.ShouldBe(1);
        packages.Applied.ShouldBe(1, "Velopack's updater applies it after the app has closed");
        restarter.Closed.ShouldBe(1);
        restarter.Started.ShouldBeEmpty("the installed edition never replaces its own program file");
        File.ReadAllText(program).ShouldBe("the old program");

        // A package whose SHA-256 the notes do not confirm: not installable, nothing downloaded.
        packages.Update = new InstalledUpdate("0.1.18", full with { Sha256 = new string('e', 64) }, []);
        var refused = (await updater.CheckAsync(true, CancellationToken.None)).ShouldNotBeNull();
        refused.Problem.ShouldBe("no_hash");
        refused.Installed.ShouldBeNull();
        await Should.ThrowAsync<AppUpdateException>(() => updater.InstallAsync(refused, null, CancellationToken.None));
        packages.Downloaded.ShouldBe(1);

        // Velopack finds no packages for the newest release (a release without its index): the release page instead.
        packages.Update = null;
        (await updater.CheckAsync(true, CancellationToken.None))!.Problem.ShouldBe("no_asset");
    }

    [Fact]
    public async Task InstallingFromThePortableProgramRunsTheCheckedSetupOfThisVersionAndCloses()
    {
        var setup = Encoding.UTF8.GetBytes("the installer");
        var running = Release("v0.1.17", $"## SHA-256\n{Hash(setup)}  {ReleaseAssetNames.SetupWindows}\n");
        running.Assets.Add(Asset("v0.1.17", ReleaseAssetNames.SetupWindows, setup.Length));
        var launcher = new FakeSetupLauncher();
        var restarter = new FakeRestarter();
        using var h = Harness(new FakeGitHub(Release("v0.1.18", "newer"), running), out var program, out _, restarter, new FlakyServer(setup),
            s => s.AddSingleton<ISetupLauncher>(launcher));

        await Installer(h).InstallAsync(null, CancellationToken.None);

        var started = launcher.Started.ShouldHaveSingleItem();
        Path.GetFileName(started).ShouldBe(ReleaseAssetNames.SetupWindows, "the running version's own Setup, not the newest release's");
        File.ReadAllBytes(started).ShouldBe(setup);
        restarter.Closed.ShouldBe(1, "this copy closes so the installed one can open the data folder");
        File.ReadAllText(program).ShouldBe("the old program", "the portable file stays");
    }

    [Fact]
    public async Task ASetupWithoutItsSha256InTheNotesIsNeverStarted()
    {
        var running = Release("v0.1.17", "No checksums here.");
        running.Assets.Add(Asset("v0.1.17", ReleaseAssetNames.SetupWindows, 3));
        var launcher = new FakeSetupLauncher();
        using var h = Harness(new FakeGitHub(running), out _, out _, more: s => s.AddSingleton<ISetupLauncher>(launcher));

        (await Should.ThrowAsync<AppUpdateException>(() => Installer(h).InstallAsync(null, CancellationToken.None))).Code.ShouldBe("no_hash");
        launcher.Started.ShouldBeEmpty();

        using var offline = Harness(new FakeGitHub(Release("v0.1.18", "only a newer one")), out _, out _);
        (await Should.ThrowAsync<AppUpdateException>(() => Installer(offline).InstallAsync(null, CancellationToken.None))).Code.ShouldBe("no_release");
    }

    private static EditionInstaller Installer(DesktopHarness h) =>
        new(h.Get<ReleaseFeed>(), h.Get<ToolDownloader>(), h.Get<SelfReplacer>(), h.Get<IAppEdition>(), h.Get<ISetupLauncher>(), h.Get<IAppRestarter>(),
            h.Get<TMConcept.Desktop.Startup.AppSession>(), NullLogger<EditionInstaller>.Instance)
        {
            Current = "0.1.17",
            Rid = "win-x64",
        };

    private static GitHubAsset Asset(string tag, string name, long size) =>
        new() { Name = name, DownloadUrl = AppUpdater.DownloadPrefix + tag + "/" + name, Size = size };

    private static AppUpdater Updater(DesktopHarness h) => new(h.Get<ReleaseFeed>(), h.Get<SelfReplacer>(), h.Get<ToolDownloader>(), h.Get<UiPreferencesFile>(),
        h.Get<IAppRestarter>(), h.Get<TMConcept.Desktop.Startup.AppSession>(), h.Get<TMConcept.Speech.Speech.SpeechWorkerClient>(), h.Get<TimeProvider>(),
        h.Get<IAppEdition>(), h.Get<IInstalledUpdates>(), NullLogger<AppUpdater>.Instance)
    {
        Current = "0.1.17",
        Rid = "win-x64",
    };

    /// <summary>A harness with a fake GitHub and download server, a packaged program in a temp folder, a fake clock and restarter.</summary>
    private static DesktopHarness Harness(FakeGitHub github, out string program, out FakeTimeProvider clock, FakeRestarter? restarter = null,
        FlakyServer? downloads = null, Action<IServiceCollection>? more = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "program-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "TMConcept-0.1.17-win-x64.exe");
        File.WriteAllText(file, "the old program");
        program = file;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        clock = time;
        return new DesktopHarness(s =>
        {
            s.AddSingleton(new UpdateTransport(() => github));
            s.AddSingleton<TimeProvider>(time);
            s.AddSingleton(new SelfReplacer(NullLogger<SelfReplacer>.Instance) { ProgramPath = file });
            s.AddSingleton<IAppRestarter>(restarter ?? new FakeRestarter());
            if (downloads is not null)
            {
                s.AddSingleton(new DownloadTransport(() => downloads));
            }

            more?.Invoke(s);
        });
    }

    private static GitHubRelease Release(string tag, string notes) => new()
    {
        Tag = tag,
        Name = "TM Concept " + tag,
        Body = notes,
        Page = "https://github.com/TM-AutoClip/tm-concept/releases/tag/" + tag,
        Assets =
        [
            new GitHubAsset
            {
                Name = $"TMConcept-{tag.TrimStart('v')}-win-x64.exe",
                DownloadUrl = $"{AppUpdater.DownloadPrefix}{tag}/TMConcept-{tag.TrimStart('v')}-win-x64.exe",
                Size = NewProgram.Length,
            },
            new GitHubAsset
            {
                Name = $"tm-concept-{tag.TrimStart('v')}-linux-x64.tar.gz",
                DownloadUrl = $"{AppUpdater.DownloadPrefix}{tag}/tm-concept-{tag.TrimStart('v')}-linux-x64.tar.gz",
                Size = 1,
            },
        ],
    };

    private static string Notes(byte[] program) => $"""
        ## TM Concept 0.1.18
        - Faster images.

        ## SHA-256
        {Hash(program)}  TMConcept-0.1.18-win-x64.exe  (0.1 MiB)
        """;

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static byte[] TarGz(params (string Name, byte[] Data)[] files)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, data) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) };
                tar.WriteEntry(entry);
            }
        }

        return memory.ToArray();
    }

    private sealed class FakeInstalledUpdates : IInstalledUpdates
    {
        public InstalledUpdate? Update { get; set; }

        public int Downloaded { get; private set; }

        public int Applied { get; private set; }

        public Task<InstalledUpdate?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(Update);

        public Task DownloadAsync(InstalledUpdate update, Action<int> percent, CancellationToken cancellationToken)
        {
            Downloaded++;
            percent(100);
            return Task.CompletedTask;
        }

        public void ApplyAfterExit(InstalledUpdate update, IReadOnlyList<string> restartArguments) => Applied++;
    }

    private sealed class FakeSetupLauncher : ISetupLauncher
    {
        public List<string> Started { get; } = [];

        public void Start(string setup) => Started.Add(setup);
    }

    private sealed class FakeRestarter : IAppRestarter
    {
        public List<string> Started { get; } = [];

        public int Closed { get; private set; }

        public void Close() => Closed++;

        public bool Fail { get; init; }

        public void Restart(string program)
        {
            Started.Add(program);
            if (Fail)
            {
                throw new System.ComponentModel.Win32Exception(5, "Access is denied.");
            }
        }
    }

    /// <summary>
    /// GitHub's release list: the releases (newest first) with ETag "r1" while the newest is v0.1.18 (304 for it), pages of
    /// <see cref="PageSize"/> with a Link header when it is set, or a rate-limit refusal (from page <see cref="LimitedFromPage"/> on).
    /// </summary>
    private sealed class FakeGitHub(params GitHubRelease[] releases) : HttpMessageHandler
    {
        public List<GitHubRelease> Releases { get; set; } = [.. releases];

        public DateTimeOffset? Limited { get; set; }

        public int LimitedFromPage { get; set; } = 1;

        public int PageSize { get; set; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var page = int.TryParse(query["page"], out var number) ? number : 1;
            if (Limited is { } reset && page >= LimitedFromPage)
            {
                var refused = new HttpResponseMessage(HttpStatusCode.Forbidden);
                refused.Headers.Add("X-RateLimit-Remaining", "0");
                refused.Headers.Add("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Task.FromResult(refused);
            }

            var newest = Releases.FirstOrDefault()?.Tag ?? "none";
            var etag = newest == "v0.1.18" ? "\"r1\"" : "\"r-" + newest + "\"";
            if (page == 1 && request.Headers.IfNoneMatch.Any(t => t.Tag == etag))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            var size = PageSize > 0 ? PageSize : Math.Max(Releases.Count, 1);
            var slice = Releases.Skip((page - 1) * size).Take(size).ToList();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(slice), Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
            if (page * size < Releases.Count)
            {
                var next = $"https://api.github.com/repositories/1/releases?per_page={size}&page={page + 1}";
                response.Headers.Add("Link", $"<{next}>; rel=\"next\", <https://api.github.com/repositories/1/releases?page=9>; rel=\"last\"");
            }

            return Task.FromResult(response);
        }
    }
}

using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Updates;
using TMConcept.Desktop.ViewModels;
using TMConcept.Tests.Architecture;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// "What's new" (0.1.23): after the app version changed, the notes of every version since the one that ran before, newest first, once, in
/// the app's language, from the notes built into the app; nothing on a new install or when nothing changed; Settings → General opens it.
/// </summary>
public sealed class WhatsNewTests
{
    private static readonly IReadOnlyList<VersionNotes> Available = Enumerable.Range(15, 9)
        .Select(p => new VersionNotes(Version("0.1." + p), "## English\n\nNotes " + p + "\n\n## Türkçe\n\nNotlar " + p, null)).ToList();

    [Fact]
    public void ComingFrom0117TheNotesOf0118To0123AreShownNewestFirst() =>
        Shown(WhatsNewRange.Versions("0.1.17", null, "0.1.23", Available)).ShouldBe(["0.1.23", "0.1.22", "0.1.21", "0.1.20", "0.1.19", "0.1.18"]);

    [Theory]
    [InlineData("0.1.23", "", "0.1.23")]
    [InlineData("", "", "0.1.23")]
    [InlineData(null, null, "0.1.23")]
    [InlineData("not a version", "", "0.1.23")]
    [InlineData("0.1.17", "0.1.23", "0.1.23")]
    [InlineData("0.1.24", "", "0.1.23")]
    public void NothingIsShownForTheSameVersionANewInstallOrNotesAlreadySeen(string? previous, string? seen, string current) =>
        WhatsNewRange.Versions(previous, seen, current, Available).ShouldBeEmpty();

    [Fact]
    public void NotesSeenAlreadyMoveTheStartForward() =>
        Shown(WhatsNewRange.Versions("0.1.17", "0.1.21", "0.1.23", Available)).ShouldBe(["0.1.23", "0.1.22"]);

    [Fact]
    public void EveryReleaseNotesFileIsBuiltInAndReadInBothLanguages()
    {
        var files = Directory.EnumerateFiles(Path.Combine(RepositoryFiles.Root, "docs", "release-notes"), "v*.md").Select(Path.GetFileName).ToList();
        BundledReleaseNotes.ResourceNames.Count.ShouldBe(files.Count, "every docs/release-notes/v*.md is embedded");
        files.Count.ShouldBeGreaterThan(20);
        var versions = files.Where(f => !f!.EndsWith(".tr.md", StringComparison.Ordinal)).Select(f => Version(f![1..^3])).ToList();
        BundledReleaseNotes.All.Select(n => n.Version).ShouldBe(versions.OrderByDescending(v => v), "every version once, newest first");

        foreach (var notes in BundledReleaseNotes.All)
        {
            var english = ReleaseNotesText.For(notes.Body, "en");
            var turkish = ReleaseNotesText.For(notes.Body, "tr");
            english.ShouldNotBeNullOrWhiteSpace(notes.Version.ToString());
            turkish.ShouldNotBeNullOrWhiteSpace(notes.Version.ToString());
            turkish.ShouldNotBe(english, $"{notes.Version}: the Turkish interface reads the Turkish part");
            english.ShouldNotContain("## ");
        }

        BundledReleaseNotes.Of(Version(AppIdentity.ShortVersion)).ShouldNotBeNull("the running version's notes are built in (write docs/release-notes/v<version>.md)");
        ReleaseNotesText.For(BundledReleaseNotes.Of(Version("0.1.0"))!.Body, "tr").ShouldContain("Yenilikler", Case.Sensitive,
            "0.1.0 has a Turkish file of its own");
    }

    [AvaloniaFact]
    public async Task TheStartShowsTheNewNotesOnceInTheAppsLanguage()
    {
        var dialog = new FakeWhatsNewDialog();
        using var h = new DesktopHarness(s => s.AddSingleton<IWhatsNewDialog>(dialog));
        var check = h.Get<WhatsNewCheck>();
        var current = BundledReleaseNotes.All[0].Version.ToString();
        var previous = BundledReleaseNotes.All[2].Version.ToString();

        await check.RunAsync(previous, current);

        Shown(dialog.Shown.Single()).ShouldBe([current, BundledReleaseNotes.All[1].Version.ToString()]);
        h.Get<UiPreferencesFile>().Read().WhatsNewSeenVersion.ShouldBe(current);

        await check.RunAsync(previous, current);
        dialog.Shown.Count.ShouldBe(1, "the same notes are shown once");

        var text = h.Get<Localizer>();
        text.Use("tr");
        var model = new WhatsNewViewModel(dialog.Shown[0], text);
        model.Title.ShouldBe("Yenilikler");
        model.Versions.Select(v => v.Header).ShouldBe([current, BundledReleaseNotes.All[1].Version.ToString()]);
        model.Versions[0].Notes.ShouldBe(ReleaseNotesText.For(BundledReleaseNotes.All[0].Body, "tr"));
    }

    [AvaloniaFact]
    public async Task ANewInstallShowsNothingButRemembersTheVersion()
    {
        var dialog = new FakeWhatsNewDialog();
        using var h = new DesktopHarness(s => s.AddSingleton<IWhatsNewDialog>(dialog));

        await h.Get<WhatsNewCheck>().RunAsync(string.Empty, "0.1.23");

        dialog.Shown.ShouldBeEmpty();
        h.Get<UiPreferencesFile>().Read().WhatsNewSeenVersion.ShouldBe("0.1.23");
    }

    [AvaloniaFact]
    public async Task SettingsGeneralOpensEveryVersionUpToTheRunningOne()
    {
        var dialog = new FakeWhatsNewDialog();
        using var h = new DesktopHarness(s => s.AddSingleton<IWhatsNewDialog>(dialog));

        await h.Get<GeneralSettingsViewModel>().WhatsNewCommand.ExecuteAsync(null);

        var shown = dialog.Shown.Single();
        shown.Count.ShouldBe(BundledReleaseNotes.All.Count(n => n.Version <= Version(AppIdentity.ShortVersion)));
        shown[0].Version.ShouldBe(Version(AppIdentity.ShortVersion));
    }

    private static AppVersion Version(string text) => AppVersion.TryParse(text, out var version) ? version : throw new ArgumentException(text);

    private static List<string> Shown(IEnumerable<VersionNotes> versions) => versions.Select(v => v.Version.ToString()).ToList();

    private sealed class FakeWhatsNewDialog : IWhatsNewDialog
    {
        public List<IReadOnlyList<VersionNotes>> Shown { get; } = [];

        public Task ShowAsync(IReadOnlyList<VersionNotes> versions)
        {
            Shown.Add(versions);
            return Task.CompletedTask;
        }
    }
}

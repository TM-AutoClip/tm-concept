using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Reports;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;

namespace TMConcept.Tests.Desktop;

/// <summary>"Report a problem": the zip holds only the kept log files (scrubbed) and system.txt; the issue address; nothing opens a browser.</summary>
public sealed class ReportTests
{
    [Fact]
    public void TheZipHoldsOnlyTheKeptLogFilesScrubbedAndSystemTxt()
    {
        using var h = new DesktopHarness();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var logs = h.Paths.LogDirectory;
        Append(Path.Combine(logs, LogFileName.For(LogRules.AppKind, today)),
            "opened " + Path.Combine(home, "Videos", "a.mp4") + " for someone@example.org with Authorization: Bearer abcdefgh12345678");
        Append(Path.Combine(logs, LogFileName.For(LogRules.SpeechWorkerKind, today)), "spoke");
        Append(Path.Combine(logs, LogFileName.For(LogRules.AppKind, today.AddDays(-6))), "six days ago");
        Append(Path.Combine(logs, LogFileName.For(LogRules.AppKind, today.AddDays(-8))), "eight days ago");
        Append(Path.Combine(logs, "notes.txt"), "not a log");
        File.WriteAllText(Path.Combine(h.Paths.DataDirectory, "concept.db"), "database");

        var zip = h.Get<ProblemReport>().Create();

        Path.GetDirectoryName(zip).ShouldBe(h.Paths.ReportDirectory);
        Path.GetFileName(zip).ShouldMatch(@"^tm-concept-logs-\d{8}-\d{4}\.zip$");
        using var archive = ZipFile.OpenRead(zip);
        archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ShouldBe(new[]
        {
            "logs/" + LogFileName.For(LogRules.AppKind, today.AddDays(-6)),
            "logs/" + LogFileName.For(LogRules.AppKind, today),
            "logs/" + LogFileName.For(LogRules.SpeechWorkerKind, today),
            ReportRules.SystemFile,
        }.Order(StringComparer.Ordinal));
        var all = string.Join('\n', archive.Entries.Select(Read));
        all.ShouldContain("six days ago");
        all.ShouldNotContain("eight days ago");
        all.ShouldNotContain("database");
        all.ShouldNotContain(home, Case.Insensitive);
        all.ShouldNotContain(h.Paths.DataDirectory, Case.Insensitive);
        all.ShouldNotContain("someone@example.org");
        all.ShouldNotContain("abcdefgh12345678");
        all.ShouldContain("~" + Path.DirectorySeparatorChar + "Videos");

        var system = Read(archive.GetEntry(ReportRules.SystemFile)!);
        system.ShouldContain("App: TM Concept ");
        system.ShouldContain("OS: ");
        system.ShouldContain("Architecture: ");
        system.ShouldContain(".NET: ");
        system.ShouldContain("Interface language: ");
        system.ShouldContain("Drawing: ");
        system.ShouldContain("Data folder: portable");
        system.ShouldContain("  Node: ");
        system.ShouldContain("Remotion: ");
    }

    [Fact]
    public void OnlyTheNewestFiveZipsAreKept()
    {
        using var h = new DesktopHarness();
        Directory.CreateDirectory(h.Paths.ReportDirectory);
        for (var day = 1; day <= 7; day++)
        {
            File.WriteAllText(Path.Combine(h.Paths.ReportDirectory, $"tm-concept-logs-202601{day:00}-1200.zip"), "old");
        }

        File.WriteAllText(Path.Combine(h.Paths.ReportDirectory, "keep-me.zip"), "not ours");
        var zip = h.Get<ProblemReport>().Create();

        var left = Directory.EnumerateFiles(h.Paths.ReportDirectory, "tm-concept-logs-*.zip").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        left.Count.ShouldBe(5);
        left.ShouldContain(Path.GetFileName(zip));
        left.ShouldNotContain("tm-concept-logs-20260103-1200.zip");
        File.Exists(Path.Combine(h.Paths.ReportDirectory, "keep-me.zip")).ShouldBeTrue();
    }

    [Fact]
    public void TheIssueAddressIsEscapedAndShort()
    {
        var address = IssueLink.Build([("version", "0.1.4+abc def"), ("os", "Windows & Co #1 İ"), ("architecture", new string('x', 20_000)), ("empty", " ")]);

        var text = address.AbsoluteUri;
        text.ShouldStartWith("https://github.com/TM-AutoClip/tm-concept/issues/new?template=bug_report.yml&");
        text.ShouldContain("version=0.1.4%2Babc%20def");
        text.ShouldContain("os=Windows%20%26%20Co%20%231%20%C4%B0");
        text.ShouldEndWith("&architecture=" + new string('x', ReportRules.MaxFieldLength));
        text.ShouldNotContain("empty=");
        text.Length.ShouldBeLessThan(ReportRules.MaxAddressLength);

        var own = IssueLink.ForThisProgram().AbsoluteUri;
        own.ShouldContain("&version=" + Uri.EscapeDataString(AppIdentity.Version));
        own.ShouldContain("&os=");
        own.ShouldContain("&architecture=");
    }

    [Fact]
    public async Task TheButtonsPrepareRevealOpenTheFormAndNeverSendAnything()
    {
        var launcher = new FakeLauncher();
        using var h = new DesktopHarness(s => s.AddSingleton<IShellLauncher>(launcher));
        var model = h.Get<ReportProblemViewModel>();

        await model.PrepareLogsCommand.ExecuteAsync(null);
        File.Exists(model.ZipPath).ShouldBeTrue();
        launcher.Revealed.ShouldBe(new[] { model.ZipPath });
        model.Status.ShouldContain(Path.GetFileName(model.ZipPath));

        await model.ReportOnGitHubCommand.ExecuteAsync(null);
        launcher.Opened.Single().ShouldBe(IssueLink.ForThisProgram());
        launcher.Opened.Single().Host.ShouldBe("github.com");
        model.HasStatus.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task TheMainWindowHasTheReportButton()
    {
        using var h = new DesktopHarness();
        await FirstRunTests.CreateAsync(h);
        await h.Get<AppSession>().LoadAsync(CancellationToken.None);
        var shell = h.Get<ShellViewModel>();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        var button = window.FindControl<Button>("ReportProblemButton");
        button.ShouldNotBeNull();
        button.Command.ShouldBe(shell.ReportProblemCommand);
        window.Close();
    }

    private static void Append(string file, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(line);
    }

    private static string Read(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>Records what would have been opened; opens nothing.</summary>
    private sealed class FakeLauncher : IShellLauncher
    {
        public List<Uri> Opened { get; } = [];

        public List<string> Revealed { get; } = [];

        public void Open(Uri address) => Opened.Add(address);

        public void OpenPath(string path)
        {
        }

        public void Reveal(string file) => Revealed.Add(file);
    }
}

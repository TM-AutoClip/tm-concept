using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// The detailed log page: every entry of the kept log files (the app's and the speech worker's), by day, filtered by level, text and session,
/// with today's files followed live; the activity panel's "Show all" opens it.
/// </summary>
public sealed class LogPageTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestFolders.Root, "logs-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = DateTimeOffset.Now;
    private readonly DateTimeOffset _before;

    public LogPageTests()
    {
        using var process = Process.GetCurrentProcess();
        _before = new DateTimeOffset(process.StartTime).AddMinutes(-30);
        if (_before.Date != _now.Date)
        {
            _before = new DateTimeOffset(_now.Date, _now.Offset).AddSeconds(1);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Cleaned with the temp folder later.
        }
    }

    [Fact]
    public void AnEntryKeepsTheLinesOfItsExceptionAndAStartHeaderTakesTheNextTime()
    {
        var at = new DateTimeOffset(2026, 11, 16, 14, 3, 21, 517, TimeSpan.FromHours(3));
        var text = "=== TM Concept 0.1.17+abc · app · Windows · X64 · .NET 10 ===\n"
            + LogLine.Format(at, LogLevel.Error, "TMConcept.Core.Videos.VideoPipeline", new EventId(7), "The render failed: code 1", null) + "\n"
            + "System.IO.IOException: disk full\n   at TMConcept.Core.Videos.VideoPipeline.RunAsync()\n"
            + LogLine.Format(at.AddSeconds(1), LogLevel.Information, "TMConcept.Desktop.Program", default, "Done: a: b", null) + "\n";

        var entries = LogFileReader.Parse(text, LogRules.AppKind);

        entries.Count.ShouldBe(3);
        entries[0].Message.ShouldStartWith("TM Concept 0.1.17");
        entries[0].At.ShouldBe(at, "the header takes the time of the entry after it");
        entries[1].Level.ShouldBe("Error");
        entries[1].Category.ShouldBe("TMConcept.Core.Videos.VideoPipeline");
        entries[1].ShortCategory.ShouldBe("VideoPipeline");
        entries[1].EventId.ShouldBe(7);
        entries[1].Detail.ShouldBe("System.IO.IOException: disk full\n   at TMConcept.Core.Videos.VideoPipeline.RunAsync()");
        entries[1].IsError.ShouldBeTrue();
        entries[2].Message.ShouldBe("Done: a: b");
    }

    [Fact]
    public void ADayShowsTheAppAndTheSpeechWorkerByTime()
    {
        var day = DateOnly.FromDateTime(_now.DateTime);
        Write(LogRules.AppKind, day, Line(_now.AddSeconds(-30), LogLevel.Information, "first"), Line(_now.AddSeconds(-10), LogLevel.Warning, "third"));
        Write(LogRules.SpeechWorkerKind, day, Line(_now.AddSeconds(-20), LogLevel.Information, "second"));
        Write(LogRules.AppKind, day.AddDays(-1), Line(_now.AddDays(-1), LogLevel.Information, "yesterday"));

        var reader = new LogFileReader(Path.Combine(_folder, "logs"));
        reader.Days().ShouldBe([day, day.AddDays(-1)]);
        var (entries, _) = reader.ReadDay(day);

        entries.Select(e => e.Message).ShouldBe(["first", "second", "third"]);
        entries.Select(e => e.Source).ShouldBe([LogRules.AppKind, LogRules.SpeechWorkerKind, LogRules.AppKind]);
    }

    [AvaloniaFact]
    public async Task ThePageFiltersByLevelTextAndSessionAndFollowsTodayLive()
    {
        using var h = new DesktopHarness();
        var day = DateOnly.FromDateTime(_now.DateTime);
        Write(LogRules.AppKind, day,
            Line(_before, LogLevel.Information, "Started before this session"),
            Line(_now.AddSeconds(-5), LogLevel.Warning, "The price list could not be read"),
            Line(_now.AddSeconds(-4), LogLevel.Error, "The render failed"));
        var page = Page(h);

        page.Load();

        page.Day.ShouldBe(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        page.Live.ShouldBeTrue();
        page.Lines.Count.ShouldBe(3);
        page.Level = page.Levels.Single(l => l.Value == "warning");
        page.Lines.Select(l => l.Message).ShouldBe(["The price list could not be read", "The render failed"]);
        page.Level = page.Levels.Single(l => l.Value == "error");
        page.Lines.ShouldHaveSingleItem().Message.ShouldBe("The render failed");
        page.Level = page.Levels[0];
        page.Search = "PRICE";
        page.Lines.ShouldHaveSingleItem().Message.ShouldBe("The price list could not be read");
        page.Search = string.Empty;
        page.SessionOnly = true;
        page.Lines.Select(l => l.Message).ShouldNotContain("Started before this session");
        page.SessionOnly = false;

        // Live: a complete new line appears; half a line waits until it is complete.
        var file = Path.Combine(_folder, "logs", LogFileName.For(LogRules.AppKind, day));
        File.AppendAllText(file, Line(_now, LogLevel.Information, "A new line") + "\n" + "2026-11-16 14:0");
        await page.TailAsync();
        page.Lines[^1].Message.ShouldBe("A new line");
        page.Lines.Count.ShouldBe(4);
        File.AppendAllText(file, "x");
        await page.TailAsync();
        page.Lines.Count.ShouldBe(4, "an unfinished line is not read");

        page.CopyText().ShouldContain("[Error] app TMConcept.Core.Test: The render failed");
    }

    [AvaloniaFact]
    public void ShowAllInTheActivityPanelOpensTheLogPage()
    {
        var viewer = new FakeLogViewer();
        using var h = new DesktopHarness(s => s.AddSingleton<ILogViewer>(viewer));

        h.Get<ShellViewModel>().ShowAllLogsCommand.Execute(null);

        viewer.Shown.ShouldBe(1);
    }

    [AvaloniaFact]
    public void TheLogWindowShowsThePage()
    {
        using var h = new DesktopHarness();
        Write(LogRules.AppKind, DateOnly.FromDateTime(_now.DateTime), Line(_now, LogLevel.Error, "Shown in the window"));
        var page = Page(h);
        page.Load();

        var window = new TMConcept.Desktop.Views.LogWindow { DataContext = page };
        window.Show();

        window.FindControl<ListBox>("Entries")!.ItemCount.ShouldBe(1);
        window.Close();
    }

    [AvaloniaFact]
    public void ActivityLinesOfThisSessionShowTheirTitlesAndOlderOnesSayTheTitleIsNotKept()
    {
        // The developer (18 Nov 2026): "<title>: iptal edildi". The file keeps the placeholder; the page shows the title of this session's line.
        using var h = new DesktopHarness();
        var text = h.Get<Localizer>();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(_now);
        var activity = new ActivityLog(clock, NullLogger<ActivityLog>.Instance);
        activity.Add(TMConcept.Core.Activity.ActivityLevel.Warning, "#3 Greek fire: canceled", TMConcept.Core.Activity.ActivityPlaceholders.Title + ": canceled");
        activity.Add(TMConcept.Core.Activity.ActivityLevel.Info, "#4 Byzantium approved", TMConcept.Core.Activity.ActivityPlaceholders.Title + " approved");
        var category = ActivityLoggerProvider.SkippedCategory;
        Write(LogRules.AppKind, DateOnly.FromDateTime(_now.DateTime),
            LogLine.Format(_now, LogLevel.Warning, category, default, "Activity: <title>: canceled", null),
            LogLine.Format(_now, LogLevel.Information, category, default, "Activity: <title> approved", null),
            LogLine.Format(_now.AddMinutes(-20), LogLevel.Warning, category, default, "Activity: <title>: canceled", null),
            LogLine.Format(_now, LogLevel.Warning, category, default, "Activity: \"<name>\" saved", null));
        var page = new LogViewerViewModel(new AppPaths(_folder, DesktopServices.CurrentOs), h.Get<IShellLauncher>(), text, TimeProvider.System,
            NullLogger<LogViewerViewModel>.Instance, activity);

        page.Load();

        var messages = page.Lines.Select(l => l.Message).ToList();
        messages.ShouldContain("Activity: #3 Greek fire: canceled");
        messages.ShouldContain("Activity: #4 Byzantium approved");
        messages.ShouldContain("Activity: " + text["logs.hidden.title"] + ": canceled", "a line of long ago has no kept title");
        messages.ShouldContain("Activity: \"" + text["logs.hidden.name"] + "\" saved");
        messages.ShouldNotContain(m => m.Contains("<title>", StringComparison.Ordinal) || m.Contains("<name>", StringComparison.Ordinal));
        File.ReadAllText(Directory.GetFiles(Path.Combine(_folder, "logs")).Single()).ShouldNotContain("Greek fire", Case.Sensitive, "the file never gets the title");
    }

    private LogViewerViewModel Page(DesktopHarness h) => new(new AppPaths(_folder, DesktopServices.CurrentOs), h.Get<IShellLauncher>(), h.Get<Localizer>(),
        TimeProvider.System, NullLogger<LogViewerViewModel>.Instance);

    private static string Line(DateTimeOffset at, LogLevel level, string message) =>
        LogLine.Format(at, level, "TMConcept.Core.Test", default, message, null);

    private void Write(string kind, DateOnly day, params string[] lines)
    {
        var logs = Path.Combine(_folder, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, LogFileName.For(kind, day)), string.Join("\n", lines) + "\n");
    }

    private sealed class FakeLogViewer : ILogViewer
    {
        public int Shown { get; private set; }

        public void Show() => Shown++;
    }
}

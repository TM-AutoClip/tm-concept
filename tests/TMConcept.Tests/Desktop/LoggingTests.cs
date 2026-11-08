using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Activity;
using TMConcept.Desktop.Logging;

namespace TMConcept.Tests.Desktop;

/// <summary>Daily log files: names, 7-day retention by name, rollover at midnight, the daily size limit, the scrubber and the crash entries.</summary>
public sealed class LoggingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "logs-" + Guid.NewGuid().ToString("N"));

    public LoggingTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Windows may still hold a file for a moment; the temp folder is cleaned later.
        }
    }

    [Fact]
    public void RetentionKeepsSixDayOldAndDeletesEightDayOldFilesByNameAndLeavesOtherFilesAlone()
    {
        var today = new DateOnly(2026, 11, 6);
        string[] kept = ["app-2026-10-31.log", "app-2026-10-30.log", "speech-worker-2026-11-06.log", "notes.txt", "other-2026-01-01.log",
            "app-2026-13-45.log", "app-logs-20261001-1200.zip", "tmconcept-20261030.log"];
        string[] deleted = ["app-2026-10-29.log", "speech-worker-2026-10-01.log", "tmconcept-20261028.log"];
        foreach (var name in kept.Concat(deleted))
        {
            File.WriteAllText(Path.Combine(_folder, name), "x");
        }

        // By the name, not by the time it was written: an old write time does not delete a recent file, a new one does not keep an old file.
        File.SetLastWriteTimeUtc(Path.Combine(_folder, "app-2026-10-31.log"), DateTime.UtcNow.AddDays(-60));
        File.SetLastWriteTimeUtc(Path.Combine(_folder, "app-2026-10-29.log"), DateTime.UtcNow);

        var failures = new List<string>();
        var removed = LogRetention.Sweep(_folder, today, (name, _) => failures.Add(name));

        removed.Order(StringComparer.Ordinal).ShouldBe(deleted.Order(StringComparer.Ordinal));
        failures.ShouldBeEmpty();
        foreach (var name in kept)
        {
            File.Exists(Path.Combine(_folder, name)).ShouldBeTrue(name);
        }
    }

    [Fact]
    public void AWarningIsWrittenThroughToTheDiskAtOnceOtherLinesWithinAFewSeconds()
    {
        var clock = Clock(new DateTimeOffset(2026, 11, 8, 13, 0, 0, TimeSpan.Zero));
        using var file = new DailyLogFile(_folder, LogRules.AppKind, clock);
        var start = file.DiskFlushes;

        file.Write("an information line", Microsoft.Extensions.Logging.LogLevel.Information);
        file.DiskFlushes.ShouldBe(start, "not every line costs a disk write");
        clock.Advance(LogRules.DiskFlushEvery);
        file.DiskFlushes.ShouldBe(start + 1, "a waiting line reaches the disk within a few seconds");
        clock.Advance(LogRules.DiskFlushEvery);
        file.DiskFlushes.ShouldBe(start + 1, "nothing waits: no write");

        file.Write("a warning line", Microsoft.Extensions.Logging.LogLevel.Warning);
        file.DiskFlushes.ShouldBe(start + 2, "a warning goes to the disk at once");
        new FileInfo(file.CurrentFile).Length.ShouldBeGreaterThan(0);
        Lines(LogFileName.For(LogRules.AppKind, new DateOnly(2026, 11, 8))).ShouldContain("a warning line");
    }

    [Fact]
    public void FileNamesCarryKindAndLocalDate()
    {
        LogFileName.For(LogRules.AppKind, new DateOnly(2026, 11, 6)).ShouldBe("app-2026-11-06.log");
        LogFileName.TryParse("speech-worker-2026-11-06.log", out var day).ShouldBeTrue();
        day.ShouldBe(new DateOnly(2026, 11, 6));
        LogFileName.TryParse("notify-2026-11-06.log", out _).ShouldBeFalse();
        LogFileName.TryParse("app-2026-11-06.log.bak", out _).ShouldBeFalse();
    }

    [Fact]
    public void ANewFileStartsAtMidnightWithItsHeaderAndOldFilesAreSweptThen()
    {
        var clock = Clock(new DateTimeOffset(2026, 11, 6, 23, 59, 59, TimeSpan.Zero));
        File.WriteAllText(Path.Combine(_folder, "app-2026-10-30.log"), "old");
        using var provider = new FileLoggerProvider(_folder, LogRules.AppKind, clock);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("before midnight");
        File.Exists(Path.Combine(_folder, "app-2026-10-30.log")).ShouldBeTrue("7 days old on the 6th: kept");
        clock.Advance(TimeSpan.FromSeconds(2));
        logger.LogInformation("after midnight");

        var first = Lines("app-2026-11-06.log");
        var second = Lines("app-2026-11-07.log");
        first[0].ShouldStartWith("=== TM Concept ");
        first[0].ShouldContain(" · app · ");
        first.ShouldContain(l => l.EndsWith("Test: before midnight", StringComparison.Ordinal));
        first.ShouldNotContain(l => l.Contains("after midnight", StringComparison.Ordinal));
        second[0].ShouldStartWith("=== TM Concept ");
        second.ShouldContain(l => l.Contains("2026-11-07 00:00:01", StringComparison.Ordinal) && l.Contains("[Information] Test: after midnight", StringComparison.Ordinal));
        File.Exists(Path.Combine(_folder, "app-2026-10-30.log")).ShouldBeFalse("8 days old on the 7th: deleted at the day change");
    }

    [Fact]
    public void DebugIsNotWrittenAndEveryOtherLevelIs()
    {
        using var provider = new FileLoggerProvider(_folder, LogRules.AppKind, Clock(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero)));
        var logger = provider.CreateLogger("Levels");
        logger.LogTrace("trace line");
        logger.LogDebug("debug line");
        logger.LogInformation("info line");
        logger.LogWarning("warning line");
        logger.LogError(new InvalidOperationException("boom"), "error line");
        logger.LogCritical(new EventId(7), "critical line");

        var text = string.Join('\n', Lines("app-2026-11-06.log"));
        text.ShouldNotContain("trace line");
        text.ShouldNotContain("debug line");
        text.ShouldContain("[Information] Levels: info line");
        text.ShouldContain("[Warning] Levels: warning line");
        text.ShouldContain("[Error] Levels: error line");
        text.ShouldContain("System.InvalidOperationException: boom");
        text.ShouldContain("[Critical] Levels[7]: critical line");
    }

    [Fact]
    public void AfterTheDailySizeLimitOneWarningIsWrittenAndNothingMoreUntilTheNextDay()
    {
        var clock = Clock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        using var provider = new FileLoggerProvider(_folder, LogRules.AppKind, clock, maxBytes: 4000);
        var logger = provider.CreateLogger("Big");
        for (var i = 0; i < 200; i++)
        {
            logger.LogInformation("line {Number} {Padding}", i, new string('x', 60));
        }

        var file = Path.Combine(_folder, "app-2026-11-06.log");
        var lines = Lines("app-2026-11-06.log");
        lines.Count(l => l.Contains("reached its daily size limit", StringComparison.Ordinal)).ShouldBe(1);
        lines[^1].ShouldContain("reached its daily size limit");
        new FileInfo(file).Length.ShouldBeLessThan(4000 + 300);
        lines.ShouldNotContain(l => l.Contains("line 199 ", StringComparison.Ordinal));

        clock.Advance(TimeSpan.FromDays(1));
        logger.LogInformation("next day");
        Lines("app-2026-11-07.log").ShouldContain(l => l.EndsWith("Big: next day", StringComparison.Ordinal));
    }

    [Fact]
    public void TheScrubberMasksTheHomeFolderEmailAddressesAndTokens()
    {
        var scrubber = new LogScrubber(@"C:\Users\S");
        var text = scrubber.Scrub(@"open C:\Users\S\Videos\a.mp4 and c:/users/s/x.json, json ""C:\\Users\\S\\y"", "
            + "mail jane.doe+tag@example.co.uk, Authorization: Bearer abcdefgh12345678, url ?api_key=SECRETVALUE1, sk-proj-" + new string('Q', 24)
            + ", ghp_" + new string('R', 30) + ", keep @remotion/renderer@4.0.1");

        text.ShouldNotContain("Users", Case.Insensitive);
        text.ShouldContain(@"~\Videos\a.mp4");
        text.ShouldContain("~/x.json");
        text.ShouldNotContain("jane.doe");
        text.ShouldContain("<e-mail>");
        text.ShouldNotContain("abcdefgh12345678");
        text.ShouldNotContain("SECRETVALUE1");
        text.ShouldNotContain(new string('Q', 24));
        text.ShouldNotContain(new string('R', 30));
        text.ShouldContain("@remotion/renderer@4.0.1");
    }

    [Fact]
    public void EveryWrittenEntryIsScrubbed()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var provider = new FileLoggerProvider(_folder, LogRules.SpeechWorkerKind, Clock(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero)));
        provider.CreateLogger("Scrub").LogWarning("tool said {Text}", Path.Combine(home, "secret-folder") + " from someone@example.org");

        var text = string.Join('\n', Lines("speech-worker-2026-11-06.log"));
        text.ShouldContain("~" + Path.DirectorySeparatorChar + "secret-folder");
        text.ShouldNotContain(home, Case.Insensitive);
        text.ShouldNotContain("someone@example.org");
    }

    [Fact]
    public void UnhandledExceptionsAreCritical()
    {
        var logger = new ListLogger();
        var crash = new CrashLog(logger);

        crash.Unhandled(new InvalidOperationException("one"), terminating: true);
        crash.Unhandled("not an exception", terminating: false);
        crash.Unobserved(new AggregateException(new IOException("two")));
        crash.Interface(new UnauthorizedAccessException("three"));

        logger.Entries.Count.ShouldBe(4);
        logger.Entries.ShouldAllBe(e => e.Level == LogLevel.Critical);
        logger.Entries[0].Exception.ShouldBeOfType<InvalidOperationException>();
        logger.Entries[1].Message.ShouldContain("System.String");
        logger.Entries[2].Message.ShouldContain("Unobserved task exception");
        logger.Entries[3].Message.ShouldContain("interface thread");
    }

    [Fact]
    public void TheAppComposesTheCrashLogAndWritesItsOwnDailyFile()
    {
        using var harness = new DesktopHarness();
        harness.Get<CrashLog>().ShouldNotBeNull();
        harness.Get<ILoggerFactory>().CreateLogger("Harness").LogInformation("composed");
        var file = Path.Combine(harness.Paths.LogDirectory, LogFileName.For(LogRules.AppKind, DateOnly.FromDateTime(DateTime.Now)));
        ReadShared(file).ShouldContain("Harness: composed");
    }

    [Fact]
    public void TheScrubberMasksLibraryFolderNamesFoldersOutsideHomeAndKnownNames()
    {
        var scrubber = new LogScrubber(@"C:\Users\S", library: @"C:\Users\S\Documents\TM Concept", data: @"D:\Portable Stuff\TMC",
            documents: @"\\server\home$\S\Docs");
        var text = scrubber.Scrub(@"open C:\Users\S\Documents\TM Concept\My Channel\2026-11-06-1-Greek fire\video.mp4 now; "
            + @"json ""C:\\Users\\S\\Documents\\TM Concept\\My Channel\\x.png"", folder 'c:/users/s/documents/tm concept/My Channel'; "
            + @"db D:\Portable Stuff\TMC\concept.db, doc \\server\home$\S\Docs\notes.txt, other C:\Users\S\Documents\TM Concepts\keep");

        text.ShouldContain(@"<library>\<profile>\<folder>\video.mp4 now");
        text.ShouldContain(@"""<library>\\<profile>\\<folder>""");
        text.ShouldContain("'<library>/<profile>'");
        text.ShouldContain(@"<data>\concept.db");
        text.ShouldContain(@"<documents>\notes.txt");
        text.ShouldContain(@"~\Documents\TM Concepts\keep");
        text.ShouldNotContain("My Channel");
        text.ShouldNotContain("Greek fire");
        text.ShouldNotContain("Portable Stuff");
        text.ShouldNotContain("server");

        // A data folder inside the home folder is written with ~ as before.
        new LogScrubber(@"C:\Users\S", data: @"C:\Users\S\AppData\Local\TM Concept").Scrub(@"C:\Users\S\AppData\Local\TM Concept\logs")
            .ShouldBe(@"~\AppData\Local\TM Concept\logs");

        var named = scrubber.WithNames(["Byzantium Chronicles", "Greek fire", "ab", "Ali"]);
        named.Scrub("Video \"Greek fire\" done for Byzantium Chronicles; Validation by ab, ALI said").ShouldBe(
            "Video \"<name>\" done for <name>; Validation by ab, <name> said");
    }

    [Fact]
    public void AnActivityLineThatNamesSomethingGoesToTheFileWithPlaceholders()
    {
        var logger = new ListLogger();
        var activity = new ActivityLog(TimeProvider.System, new TypedLogger(logger));

        activity.Add(ActivityLevel.Info, "Profile \"Byzantium\" created.", "Profile \"" + ActivityPlaceholders.Name + "\" created.");
        activity.Add(ActivityLevel.Warning, "Backup skipped.");

        logger.Entries[0].Message.ShouldBe("Activity: Profile \"<name>\" created.");
        logger.Entries[1].Message.ShouldBe("Activity: Backup skipped.");
        logger.Entries[1].Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void WhenTheNewDaysFileCannotBeOpenedAtFirstItsHeaderAndTheSweepComeWithTheFirstEntryThatOpensIt()
    {
        var clock = Clock(new DateTimeOffset(2026, 11, 6, 23, 59, 59, TimeSpan.Zero));
        File.WriteAllText(Path.Combine(_folder, "app-2026-10-30.log"), "old");
        var blocker = Path.Combine(_folder, "app-2026-11-07.log");
        Directory.CreateDirectory(blocker);
        using var provider = new FileLoggerProvider(_folder, LogRules.AppKind, clock);
        var logger = provider.CreateLogger("Test");
        logger.LogInformation("before midnight");

        clock.Advance(TimeSpan.FromSeconds(2));
        logger.LogInformation("lost: the file cannot be opened");
        Directory.Delete(blocker);
        logger.LogInformation("after midnight");

        var second = Lines("app-2026-11-07.log");
        second[0].ShouldStartWith("=== TM Concept ");
        second.ShouldContain(l => l.EndsWith("Test: after midnight", StringComparison.Ordinal));
        File.Exists(Path.Combine(_folder, "app-2026-10-30.log")).ShouldBeFalse("swept when the day's file was opened");
    }

    [Fact]
    public void TwoProcessesOfOneKindNeverOverwriteEachOthersLines()
    {
        var clock = Clock(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));
        using var first = new FileLoggerProvider(_folder, LogRules.SpeechWorkerKind, clock);
        using var second = new FileLoggerProvider(_folder, LogRules.SpeechWorkerKind, clock);

        // Two writers with their own file handles, as two processes have: every line ends up in the file once.
        Parallel.For(0, 2, writer =>
        {
            var logger = (writer == 0 ? first : second).CreateLogger("W" + writer);
            for (var i = 0; i < 300; i++)
            {
                logger.LogInformation("line {Number} {Padding}", i, new string('x', 40));
            }
        });

        var lines = Lines("speech-worker-2026-11-06.log");
        lines.Count(l => l.StartsWith("=== TM Concept ", StringComparison.Ordinal)).ShouldBe(2);
        lines.Count(l => l.Contains("W0: line ", StringComparison.Ordinal)).ShouldBe(300);
        lines.Count(l => l.Contains("W1: line ", StringComparison.Ordinal)).ShouldBe(300);
        lines.ShouldAllBe(l => l.StartsWith("=== ", StringComparison.Ordinal) || l.StartsWith("2026-11-06 ", StringComparison.Ordinal));
        DailyLogFile.LockName(_folder, LogRules.SpeechWorkerKind).ShouldNotContain("tmconcept-tests");
    }

    [Fact]
    public void TheShellLauncherNamesOnlyTheKindOfWhatItOpens()
    {
        ShellLauncherKind(_folder).ShouldBe("a folder");
        ShellLauncherKind(Path.Combine(_folder, "Byzantium", "2026-11-06-1-Greek fire", "Video.MP4")).ShouldBe("a .mp4 file");
    }

    private static string ShellLauncherKind(string path) => TMConcept.Desktop.Os.ShellLauncher.Kind(path);

    private static FakeTimeProvider Clock(DateTimeOffset start)
    {
        var clock = new FakeTimeProvider(start);
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        return clock;
    }

    private static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private List<string> Lines(string name) =>
        ReadShared(Path.Combine(_folder, name)).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Keeps every entry.</summary>
    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    /// <summary><see cref="ListLogger"/> as the typed logger <see cref="ActivityLog"/> takes.</summary>
    private sealed class TypedLogger(ListLogger inner) : ILogger<ActivityLog>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}

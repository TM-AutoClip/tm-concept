using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
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
}

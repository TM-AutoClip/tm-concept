using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.Processes;
using TMConcept.Engines.Processes;
using TMConcept.Tests.Documents;

namespace TMConcept.Tests.Engines;

/// <summary>
/// 0.1.59: a cancel waits until the killed program has exited and writes its last lines to the log; a run with a heartbeat writes a line
/// with the time so far and its last line at every interval (a fake clock). Uses the system's ping (Windows) or sleep (Linux); no network.
/// </summary>
public sealed class ProcessRunnerTests
{
    private static (string Program, string[] Arguments) LongRunning() => OperatingSystem.IsWindows()
        ? (Path.Combine(Environment.SystemDirectory, "PING.EXE"), ["-n", "60", "127.0.0.1"])
        : (File.Exists("/usr/bin/sleep") ? "/usr/bin/sleep" : "/bin/sleep", ["60"]);

    [Fact]
    public async Task ACancelWaitsForTheProgramToEndAndLogsItsLastLines()
    {
        using var guard = ChildProcessTests.Guard();
        var logger = new RecordingLogger<ProcessRunner>();
        var runner = new ProcessRunner(guard, logger);
        var (program, arguments) = LongRunning();
        using var cancel = new CancellationTokenSource();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = runner.RunAsync(new ProcessStart(program, arguments, Path.GetTempPath()), _ => first.TrySetResult(), cancel.Token);
        if (OperatingSystem.IsWindows())
        {
            await first.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }

        await cancel.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        var line = logger.Lines.Where(l => l.Contains("was cancelled", StringComparison.Ordinal)).ShouldHaveSingleItem();
        line.ShouldContain("its process tree was ended; its last output:", Case.Sensitive, "the program had exited when the call returned");
        if (OperatingSystem.IsWindows())
        {
            line.ShouldContain("127.0.0.1", Case.Sensitive, "ping's lines are in the log after a cancel");
        }

        guard.KillAll().ShouldBe(0, "nothing of it still runs");
    }

    [Fact]
    public async Task AHeartbeatLineGoesToTheLogAtEveryInterval()
    {
        using var guard = ChildProcessTests.Guard();
        var logger = new RecordingLogger<ProcessRunner>();
        var clock = new FakeTimeProvider();
        var runner = new ProcessRunner(guard, logger, clock);
        var (program, arguments) = LongRunning();
        using var cancel = new CancellationTokenSource();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = runner.RunAsync(new ProcessStart(program, arguments, Path.GetTempPath()) { Heartbeat = TimeSpan.FromSeconds(60) },
            _ => first.TrySetResult(), cancel.Token);
        if (OperatingSystem.IsWindows())
        {
            await first.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }

        // The timer is made right after the start: wait until it is there before the clock moves.
        for (var i = 0; i < 100 && !logger.Lines.Any(l => l.Contains("started", StringComparison.Ordinal)); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        await Task.Delay(200, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(60));
        clock.Advance(TimeSpan.FromSeconds(60));

        var beats = logger.Lines.Where(l => l.Contains("still runs after", StringComparison.Ordinal)).ToList();
        beats.Count.ShouldBe(2);
        beats[1].ShouldContain("after 2 min", Case.Sensitive);
        if (OperatingSystem.IsWindows())
        {
            beats[1].ShouldContain("127.0.0.1", Case.Sensitive, "the last line is in the heartbeat");
        }

        await cancel.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        clock.Advance(TimeSpan.FromSeconds(60));
        logger.Lines.Count(l => l.Contains("still runs after", StringComparison.Ordinal)).ShouldBe(2, "no heartbeat after the end");
    }

    [Fact]
    public async Task ProgressLinesGoToTheHandlerButNotIntoTheKeptLastLines()
    {
        using var guard = ChildProcessTests.Guard();
        var runner = new ProcessRunner(guard, new RecordingLogger<ProcessRunner>());
        var (program, arguments) = OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/d", "/c", "echo TMC-P: 1/1 1/2 steps&echo a real line&echo TMC-P: 1/1 2/2 steps" })
            : ("/bin/sh", ["-c", "echo 'TMC-P: 1/1 1/2 steps'; echo 'a real line'; echo 'TMC-P: 1/1 2/2 steps'"]);
        var seen = new List<string>();

        var result = await runner.RunAsync(new ProcessStart(program, arguments, Path.GetTempPath()) { ProgressMark = "TMC-P:" },
            line => { lock (seen) { seen.Add(line.Trim()); } }, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0);
        seen.Count(l => l.StartsWith("TMC-P:", StringComparison.Ordinal)).ShouldBe(2, "the handler gets every line");
        result.LastLines.ShouldContain("a real line");
        result.LastLines.ShouldNotContain("TMC-P:", Case.Sensitive, "a line a second would push a failure's own lines out");
    }
}

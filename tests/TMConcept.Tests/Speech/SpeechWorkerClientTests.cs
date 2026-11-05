using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Shouldly;
using TMConcept.Core.Speech;
using TMConcept.Speech.Speech;
using TMConcept.Tests.Engines;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Speech;

/// <summary>
/// The client side of the speech worker, with real processes: the app's own program (next to the tests) as a worker without its libraries,
/// and a stand-in worker that never answers (a batch file running ping on Windows, a shell script running sleep on Linux). No network.
/// </summary>
public sealed class SpeechWorkerClientTests
{
    [Fact]
    public async Task AModelFailureIsLoggedWithTheWorkersOwnReason()
    {
        var program = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "TMConcept.exe" : "TMConcept");
        Assert.SkipUnless(File.Exists(program), "The app program is not next to the tests.");
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.WorkDirectory);
        using var guard = ChildProcessTests.Guard();
        var logger = new RecordingLogger();

        // The reason comes on the worker's error output, the answer on its standard output; the log line must have the reason every time.
        for (var run = 0; run < 3; run++)
        {
            using var worker = new SpeechWorkerClient(guard, logger) { ProgramPath = program };
            var reply = await worker.SpeakAsync(Path.Combine(paths.ToolsDirectory, "none"), paths.WorkDirectory, 1, Request(paths),
                TestContext.Current.CancellationToken);

            reply.Ok.ShouldBeFalse();
            reply.Code.ShouldBe("model");
            worker.Running.ShouldBeFalse();
            logger.Lines(LogLevel.Error).Last().ShouldContain("FileNotFoundException");
        }

        logger.Lines(LogLevel.Warning).ShouldContain(l => l.Contains("speech worker: FileNotFoundException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACancelDuringASceneKillsTheWorkerAtOnce()
    {
        using var paths = new TempPaths();
        using var guard = ChildProcessTests.Guard();
        using var worker = new SpeechWorkerClient(guard, new RecordingLogger()) { ProgramPath = SilentWorker(paths) };
        var request = Request(paths);
        Directory.CreateDirectory(paths.WorkDirectory);
        await File.WriteAllTextAsync(request.OutputWav, "half", TestContext.Current.CancellationToken);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));
        var clock = Stopwatch.StartNew();

        await Should.ThrowAsync<OperationCanceledException>(() => worker.SpeakAsync(paths.ToolsDirectory, paths.WorkDirectory, 1, request, cancel.Token));

        // The stand-in would stay 60 seconds; the cancel ends it, not the end of the scene.
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        worker.Running.ShouldBeFalse();
        File.Exists(request.OutputWav).ShouldBeFalse();
    }

    [Fact]
    public async Task ClosingWhileASceneIsSpokenEndsTheRequestWithASpeechError()
    {
        using var paths = new TempPaths();
        using var guard = ChildProcessTests.Guard();
        var worker = new SpeechWorkerClient(guard, new RecordingLogger()) { ProgramPath = SilentWorker(paths) };
        var speaking = worker.SpeakAsync(paths.ToolsDirectory, paths.WorkDirectory, 1, Request(paths), CancellationToken.None);
        var clock = Stopwatch.StartNew();
        while (!worker.Running && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        worker.Running.ShouldBeTrue();
        await Task.Delay(300, TestContext.Current.CancellationToken);

        // Dispose ends the worker and does not leave a late Release on a disposed gate behind.
        Should.NotThrow(worker.Dispose);

        await Should.ThrowAsync<SpeechException>(() => speaking.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        Should.NotThrow(worker.Dispose);
    }

    private static SpeechWorkerRequest Request(TempPaths paths) =>
        new("Merhaba", "tr", 0, 1, 8, Path.Combine(paths.WorkDirectory, "scene.wav"));

    /// <summary>A stand-in worker that reads nothing and answers nothing for 60 seconds.</summary>
    private static string SilentWorker(TempPaths paths)
    {
        Directory.CreateDirectory(paths.WorkDirectory);
        if (OperatingSystem.IsWindows())
        {
            var batch = Path.Combine(paths.WorkDirectory, "silent-worker.cmd");
            File.WriteAllText(batch, "@\"%SystemRoot%\\System32\\PING.EXE\" -n 60 127.0.0.1 >nul\r\n");
            return batch;
        }

        var script = Path.Combine(paths.WorkDirectory, "silent-worker.sh");
        File.WriteAllText(script, "#!/bin/sh\nexec sleep 60\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    /// <summary>Keeps the formatted log lines with their level.</summary>
    private sealed class RecordingLogger : ILogger<SpeechWorkerClient>
    {
        private readonly List<(LogLevel Level, string Line)> _lines = [];

        public IReadOnlyList<string> Lines(LogLevel level)
        {
            lock (_lines)
            {
                return _lines.Where(l => l.Level == level).Select(l => l.Line).ToList();
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            lock (_lines)
            {
                _lines.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Processes;
using TMConcept.Engines.Processes;

namespace TMConcept.Tests.Engines;

/// <summary>
/// Child processes end with the app: the runner hands every child to the guard (Windows: job object), KillAll ends what still runs, and
/// the start-up sweep ends only processes whose program lies inside the given tools folder. Uses a copy of the system's ping (Windows)
/// or sleep (Linux); no network.
/// </summary>
public sealed class ChildProcessTests
{
    /// <summary>The guard of this system (what the OS module registers).</summary>
    internal static ChildProcessGuardBase Guard() => OperatingSystem.IsWindows()
        ? new WindowsChildProcessGuard(NullLogger<WindowsChildProcessGuard>.Instance)
        : new LinuxChildProcessGuard(NullLogger<LinuxChildProcessGuard>.Instance);

    [Fact]
    public async Task KillAllEndsAChildTheRunnerStarted()
    {
        using var guard = Guard();
        var runner = new ProcessRunner(guard, NullLogger<ProcessRunner>.Instance);
        var (program, arguments) = LongRunning(SystemProgram());

        // The child is handed to the guard before RunAsync first waits, so it is tracked when the call returns.
        var run = runner.RunAsync(new ProcessStart(program, arguments, Path.GetTempPath()), null, CancellationToken.None);
        guard.KillAll().ShouldBe(1);

        var result = await run.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        result.ExitCode.ShouldNotBe(0);
        guard.KillAll().ShouldBe(0);
    }

    [Fact]
    public void OnWindowsAnAdoptedChildIsInTheJobAndDisposingTheGuardEndsIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The job object is Windows only.");
        var guard = Guard();
        var (program, arguments) = LongRunning(SystemProgram());
        using var process = Start(program, arguments);
        try
        {
            using (guard.Adopt(process))
            {
                guard.IsTied(process).ShouldBeTrue();
                guard.Dispose();
                process.WaitForExit(10_000).ShouldBeTrue();
            }
        }
        finally
        {
            Kill(process);
        }
    }

    [Fact]
    public void TheSweepEndsARealOrphanFromTheToolsFolderButNotAProgramThatOnlyNamesItInItsArguments()
    {
        var tools = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "sweep-" + Guid.NewGuid().ToString("N"), "tools");
        var copy = Path.Combine(tools, "bin", Path.GetFileName(SystemProgram()));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(SystemProgram(), copy);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        try
        {
            // Both are started through a shell that exits at once, so their parent is gone (an orphan, as after a hard kill). The second
            // is a system program whose arguments name the tools folder: it must live.
            Orphan(OperatingSystem.IsWindows() ? $"\"{copy}\" -n 30 127.0.0.1" : $"'{copy}' 30");
            Orphan(OperatingSystem.IsWindows()
                ? $"cmd /c \"ping -n 30 127.0.0.1 >nul & rem {tools}\""
                : $"sh -c 'sleep 30; : {tools}'");
            for (var i = 0; i < 50 && Running(copy).Count == 0; i++)
            {
                Thread.Sleep(100);
            }

            Running(copy).Count.ShouldBe(1);
            using var guard = Guard();
            guard.SweepOrphans(tools).ShouldBe(1);
            for (var i = 0; i < 50 && Running(copy).Count > 0; i++)
            {
                Thread.Sleep(100);
            }

            Running(copy).ShouldBeEmpty();
        }
        finally
        {
            foreach (var process in Running(copy))
            {
                Kill(process);
                process.Dispose();
            }

            try
            {
                Directory.Delete(Path.GetDirectoryName(tools)!, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Windows may still hold the program file for a moment; the temp folder is cleaned later.
            }
        }
    }

    [Fact]
    public void TheSweepRuleEndsOnlyToolProcessesWhoseParentIsGoneOrForeign()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmconcept-rule");
        var tools = Path.Combine(root, "data", "tools");
        string Tool(string name) => Path.Combine(tools, "node", name);
        var app = Path.Combine(root, "elsewhere", Path.GetFileName(Environment.ProcessPath)!);
        var system = Path.Combine(root, "system", "init");
        var own = Environment.ProcessId;
        var guard = new FakeTable(new Dictionary<int, (string?, int?)>
        {
            [101] = (system, 0),
            [10] = (Tool("node"), 101),           // parent is the system (subreaper / init): orphan
            [11] = (Tool("chrome"), 10),          // child of a tool: ended with its parent's tree
            [12] = (Tool("ffmpeg"), 9_999),       // parent gone: orphan
            [13] = (Path.Combine(root, "usr", "vim"), 101), // not from the tools folder (whatever its arguments say)
            [15] = (app, 101),
            [14] = (Tool("node"), 15),            // child of a running TM Concept
            [16] = (Tool("ffmpeg"), own),         // child of this process
        });

        guard.SweepOrphans(tools).ShouldBe(2);
        guard.Ended.Order().ToArray().ShouldBe(new[] { 10, 12 });
    }

    [Theory]
    [InlineData("4242 (node) S 1 4242 4242 0 -1", 1)]
    [InlineData("77 (a (b) c) R 4242 77 77 0 -1", 4242)]
    [InlineData("garbage", null)]
    public void TheParentIsReadFromProcStat(string stat, int? parent) => LinuxChildProcessGuard.ParseParent(stat).ShouldBe(parent);
    private static string SystemProgram() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.SystemDirectory, "PING.EXE")
        : File.Exists("/usr/bin/sleep") ? "/usr/bin/sleep" : "/bin/sleep";

    private static (string Program, string[] Arguments) LongRunning(string program) => OperatingSystem.IsWindows()
        ? (program, ["-n", "60", "127.0.0.1"])
        : (program, ["60"]);

    private static Process Start(string program, string[] arguments)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("The test program did not start.");
    }

    /// <summary>Starts a command through a shell that returns at once (the command keeps running without a parent).</summary>
    private static void Orphan(string command)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/d /c start \"\" /b {command}")
            : new ProcessStartInfo("/bin/sh", ["-c", command + " >/dev/null 2>&1 &"]);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using var shell = Process.Start(info) ?? throw new InvalidOperationException("The shell did not start.");
        shell.WaitForExit(10_000).ShouldBeTrue();
    }

    /// <summary>The running processes started from <paramref name="program"/>.</summary>
    private static List<Process> Running(string program)
    {
        var found = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(program)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, program, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(process);
                    continue;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Another user's or an ended process.
            }

            process.Dispose();
        }

        return found;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    /// <summary>A guard over a made-up process table; ending only records the id.</summary>
    private sealed class FakeTable(Dictionary<int, (string? Image, int? Parent)> table) : ChildProcessGuardBase(NullLogger.Instance)
    {
        public List<int> Ended { get; } = [];

        protected override StringComparison PathComparison => StringComparison.Ordinal;

        protected override void Tie(Process process)
        {
        }

        protected override string? ImagePath(int pid) => table.TryGetValue(pid, out var entry) ? entry.Image : null;

        protected override int? ParentId(int pid) => table.TryGetValue(pid, out var entry) ? entry.Parent : null;

        protected override IEnumerable<int> ProcessIds() => table.Keys;

        protected override bool EndOrphan(int pid)
        {
            Ended.Add(pid);
            return true;
        }
    }
}

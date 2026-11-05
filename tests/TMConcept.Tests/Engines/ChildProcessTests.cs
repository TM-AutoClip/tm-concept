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
    public void TheSweepEndsOnlyProcessesInsideTheToolsFolder()
    {
        var tools = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "sweep-" + Guid.NewGuid().ToString("N"), "tools");
        var copy = Path.Combine(tools, "bin", Path.GetFileName(SystemProgram()));
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(SystemProgram(), copy);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var (_, arguments) = LongRunning(SystemProgram());
        using var orphan = Start(copy, arguments);
        using var other = Start(SystemProgram(), arguments);
        try
        {
            using var guard = Guard();
            guard.SweepOrphans(tools).ShouldBe(1);
            orphan.WaitForExit(10_000).ShouldBeTrue();
            other.HasExited.ShouldBeFalse();
        }
        finally
        {
            Kill(orphan);
            Kill(other);
            try
            {
                Directory.Delete(Path.GetDirectoryName(tools)!, recursive: true);
            }
            catch (IOException)
            {
                // Windows may still hold the program file for a moment; the temp folder is cleaned later.
            }
        }
    }

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
}

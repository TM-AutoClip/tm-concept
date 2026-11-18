using System.Diagnostics;
using System.Globalization;

namespace TMConcept.Tests;

/// <summary>
/// The assembly fixture of the test run: at the start it deletes what earlier runs left in <see cref="TestFolders.Parent"/> (run folders of
/// processes that are gone, the loose folders tests made there before 0.1.28 once they are a day old, and the PDFium smoke test's
/// per-process folders whose process is gone), at the end it deletes this run's folder.
/// </summary>
public sealed class TestRunFolder : IDisposable
{
    /// <summary>
    /// The PDFium smoke test keeps its loaded library's folder (<c>pdfium-smoke/&lt;pid&gt;</c>) while its process runs: a loaded library
    /// cannot be deleted. Only the folders of processes that are gone are deleted.
    /// </summary>
    private const string KeptFolder = "pdfium-smoke";

    /// <summary>Cleans up after earlier runs.</summary>
    public TestRunFolder()
    {
        if (!Directory.Exists(TestFolders.Parent))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(TestFolders.Parent))
        {
            var name = Path.GetFileName(folder);
            if (name == KeptFolder)
            {
                foreach (var smoke in Directory.EnumerateDirectories(folder).Where(s => !Alive(Pid(Path.GetFileName(s)))))
                {
                    TestFolders.Delete(smoke);
                }

                continue;
            }

            var stale = name.StartsWith(TestFolders.RunPrefix, StringComparison.Ordinal)
                ? !Alive(RunPid(name))
                : DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > TimeSpan.FromDays(1);
            if (stale)
            {
                TestFolders.Delete(folder);
            }
        }
    }

    /// <summary>Deletes this run's folder.</summary>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (TestFolders.Made)
        {
            TestFolders.Delete(TestFolders.Root);
        }
    }

    /// <summary>The process id a run folder is named after (<c>run-&lt;pid&gt;-&lt;time&gt;</c>); 0 when the name has none.</summary>
    private static int RunPid(string name)
    {
        var parts = name.Split('-');
        return parts.Length < 3 ? 0 : Pid(parts[1]);
    }

    /// <summary>The process id <paramref name="text"/> names; 0 when it names none.</summary>
    private static int Pid(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : 0;

    /// <summary>Whether the process <paramref name="pid"/> still runs (0 = none).</summary>
    private static bool Alive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        if (pid == Environment.ProcessId)
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

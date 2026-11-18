using System.Diagnostics;
using System.Globalization;

namespace TMConcept.Tests;

/// <summary>
/// The assembly fixture of the test run: at the start it deletes what earlier runs left in <see cref="TestFolders.Parent"/> (run folders of
/// processes that are gone, and the loose folders tests made there before 0.1.28 once they are a day old; the PDFium smoke test's kept
/// folder stays), at the end it deletes this run's folder.
/// </summary>
public sealed class TestRunFolder : IDisposable
{
    /// <summary>The PDFium smoke test keeps its loaded library's folder on purpose.</summary>
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
                continue;
            }

            var stale = name.StartsWith(TestFolders.RunPrefix, StringComparison.Ordinal)
                ? !Alive(name)
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

    /// <summary>Whether the process a run folder is named after still runs (<c>run-&lt;pid&gt;-&lt;time&gt;</c>).</summary>
    private static bool Alive(string name)
    {
        var parts = name.Split('-');
        if (parts.Length < 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
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

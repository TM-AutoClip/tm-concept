using System.Globalization;

namespace TMConcept.Tests;

/// <summary>
/// The one temporary folder of this test run (<c>%TEMP%/tmconcept-tests/run-&lt;pid&gt;-&lt;time&gt;</c>): every test folder lives under it, and
/// <see cref="TestRunFolder"/> deletes it when the run ends, so a run leaves nothing in the system's temp folder (before 0.1.28 about 900
/// folders piled up there).
/// </summary>
internal static class TestFolders
{
    /// <summary>The start of a run folder's name.</summary>
    public const string RunPrefix = "run-";

    private static readonly Lazy<string> RunRoot = new(() =>
    {
        var folder = Path.Combine(Parent, RunPrefix + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "-"
            + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        return folder;
    });

    /// <summary>The folder every run folder sits in (also the place of the folders older versions of the tests left).</summary>
    public static string Parent => Path.Combine(Path.GetTempPath(), "tmconcept-tests");

    /// <summary>This run's folder (made on first use).</summary>
    public static string Root => RunRoot.Value;

    /// <summary>Whether this run made its folder.</summary>
    public static bool Made => RunRoot.IsValueCreated;

    /// <summary>A new, not yet existing folder path under this run's folder (<paramref name="prefix"/> + a GUID).</summary>
    public static string New(string prefix) => Path.Combine(Root, prefix + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Deletes a folder with everything in it: read-only files are made writable first; a file Windows still holds for a moment is tried
    /// again. Returns whether it is gone.
    /// </summary>
    public static bool Delete(string folder)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    return true;
                }

                foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(folder, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }

        return !Directory.Exists(folder);
    }
}

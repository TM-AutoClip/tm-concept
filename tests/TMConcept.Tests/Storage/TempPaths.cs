using Microsoft.Data.Sqlite;
using TMConcept.Core.Paths;

namespace TMConcept.Tests.Storage;

/// <summary>A throw-away data folder with a library folder inside it.</summary>
internal sealed class TempPaths : IAppPaths, IDisposable
{
    public TempPaths()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "tmconcept-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }

    public string DataDirectory { get; }

    public string DatabaseFile => Path.Combine(DataDirectory, "concept.db");

    public string KeyFile => Path.Combine(DataDirectory, "key.json");

    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public string ToolsDirectory => Path.Combine(DataDirectory, "tools");

    public string WorkDirectory => Path.Combine(DataDirectory, "work");

    public string LibraryDirectory => Path.Combine(DataDirectory, "library");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A file may still be open on Windows for a moment; the temp folder is cleaned by the system later.
        }
    }
}

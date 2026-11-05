using Shouldly;
using TMConcept.Desktop.Os;

namespace TMConcept.Tests.Desktop;

public sealed class LinuxFoldersTests
{
    [Fact]
    public void DocumentsFolderComesFromUserDirs()
    {
        var home = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "home-" + Guid.NewGuid().ToString("N"));
        var config = Path.Combine(home, ".config");
        Directory.CreateDirectory(config);
        try
        {
            LinuxOsModule.XdgDocuments(home, null).ShouldBe(Path.Combine(home, "Documents"));
            File.WriteAllText(Path.Combine(config, "user-dirs.dirs"), "# comment\nXDG_DOCUMENTS_DIR=\"$HOME/Belgeler\"\n");
            LinuxOsModule.XdgDocuments(home, null).ShouldBe(home + "/Belgeler");
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}

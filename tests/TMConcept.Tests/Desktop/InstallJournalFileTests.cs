using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Desktop.Components;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Paths;

namespace TMConcept.Tests.Desktop;

/// <summary>
/// 28 Nov 2026: the install journal in the data folder keeps the begun components in order, ignores one written twice, goes away when the
/// last one finished, and an unreadable file resumes nothing (each component is still offered when a step needs it).
/// </summary>
public sealed class InstallJournalFileTests : IDisposable
{
    private readonly string _folder = TestFolders.New("journal-");

    public InstallJournalFileTests()
    {
        Directory.CreateDirectory(_folder);
        Paths = new AppPaths(_folder, DesktopServices.CurrentOs);
        Journal = new InstallJournalFile(Paths, NullLogger<InstallJournalFile>.Instance);
    }

    private AppPaths Paths { get; }

    private InstallJournalFile Journal { get; }

    public void Dispose() => TestFolders.Delete(_folder);

    [Fact]
    public void BegunComponentsStayInOrderOnceEachAndTheFileGoesWithTheLast()
    {
        Journal.Unfinished().ShouldBeEmpty();

        Journal.Begin(["sd-cpp", "image.flux", "sd-cpp"]);
        Journal.Begin(["image.flux", "voice.piper", string.Empty]);

        Journal.Unfinished().ShouldBe(["sd-cpp", "image.flux", "voice.piper"]);
        new InstallJournalFile(Paths, NullLogger<InstallJournalFile>.Instance).Unfinished().ShouldBe(["sd-cpp", "image.flux", "voice.piper"],
            "it outlives the process");

        Journal.Finished("image.flux");
        Journal.Finished("unknown");
        Journal.Unfinished().ShouldBe(["sd-cpp", "voice.piper"]);

        Journal.Finished("sd-cpp");
        Journal.Finished("voice.piper");
        Journal.Unfinished().ShouldBeEmpty();
        File.Exists(Paths.InstallJournalFile).ShouldBeFalse();
        File.Exists(Paths.InstallJournalFile + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void AnUnreadableJournalResumesNothing()
    {
        File.WriteAllText(Paths.InstallJournalFile, "{ not a list");

        Journal.Unfinished().ShouldBeEmpty();

        Journal.Begin(["node"]);
        Journal.Unfinished().ShouldBe(["node"], "a new install writes a readable journal again");
    }
}

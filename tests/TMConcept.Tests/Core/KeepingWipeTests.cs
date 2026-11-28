using Shouldly;
using TMConcept.Core.DiskSpace;

namespace TMConcept.Tests.Core;

/// <summary>
/// 28 Nov 2026: what an install replaces is deleted for good except the kept paths: a kept path that is the target (or holds it) keeps
/// everything, a kept path inside the target keeps itself and the folders on the way to it while everything next to them goes.
/// </summary>
public sealed class KeepingWipeTests : IDisposable
{
    private readonly string _folder = TestFolders.New("wipe-");

    private readonly Deleting _discard = new();

    public KeepingWipeTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestFolders.Delete(_folder);

    [Fact]
    public async Task WithoutAKeptPathTheWholeTargetGoes()
    {
        var target = Tree("sd-cpp", "old/sd-cli.exe", "new/sd-cli.exe.part");
        var file = Path.Combine(_folder, "downloads", "sd.zip.part");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "half");

        (await Wipe().WipeAsync(target, [], CancellationToken.None)).ShouldBe(1);
        (await Wipe().WipeAsync(file, [], CancellationToken.None)).ShouldBe(1);
        (await Wipe().WipeAsync(Path.Combine(_folder, "missing"), [], CancellationToken.None)).ShouldBe(0);

        Directory.Exists(target).ShouldBeFalse();
        File.Exists(file).ShouldBeFalse();
    }

    [Fact]
    public async Task AKeptPathThatIsOrHoldsTheTargetKeepsEverything()
    {
        var target = Tree("music", "shared/t5.bin");

        (await Wipe().WipeAsync(target, [target + Path.DirectorySeparatorChar], CancellationToken.None)).ShouldBe(0);
        (await Wipe().WipeAsync(Path.Combine(target, "shared"), [target], CancellationToken.None)).ShouldBe(0);

        File.Exists(Path.Combine(target, "shared", "t5.bin")).ShouldBeTrue();
        _discard.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task AKeptPathInsideStaysWithTheFoldersOnTheWayAndEverythingNextToItGoes()
    {
        var target = Tree("ollama", "0.40.16/ollama.exe", "models/blobs/sha256-a", "models/blobs/sha256-b-partial", "home/id");
        var kept = Path.Combine(target, "models", "blobs", "sha256-a");

        var count = await Wipe().WipeAsync(target, [kept], CancellationToken.None);

        count.ShouldBe(3, "the old version, the home and the partial blob");
        File.Exists(kept).ShouldBeTrue();
        Directory.Exists(Path.Combine(target, "0.40.16")).ShouldBeFalse();
        Directory.Exists(Path.Combine(target, "home")).ShouldBeFalse();
        File.Exists(Path.Combine(target, "models", "blobs", "sha256-b-partial")).ShouldBeFalse();
    }

    private KeepingWipe Wipe() => new(_discard);

    private string Tree(string name, params string[] files)
    {
        var root = Path.Combine(_folder, name);
        foreach (var file in files)
        {
            var path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }

        return root;
    }

    /// <summary>Deletes for good and records what it was given.</summary>
    private sealed class Deleting : IFolderDiscard
    {
        public List<string> Deleted { get; } = [];

        public Task DiscardAsync(string path, CancellationToken cancellationToken)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }

            Deleted.Add(path);
            return Task.CompletedTask;
        }

        public bool Owns(string path) => true;
    }
}

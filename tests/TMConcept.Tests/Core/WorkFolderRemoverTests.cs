using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TMConcept.Core.DiskSpace;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.59: a job's work folder is removed even when a stopped program still holds a file in it for a moment: the delete is tried again after
/// a pause, and gives up quietly after the last try (the start-up sweep takes it later). A held file blocks a delete on Windows only.
/// </summary>
public sealed class WorkFolderRemoverTests
{
    private static string Folder()
    {
        var folder = TestFolders.New("remove-");
        Directory.CreateDirectory(Path.Combine(folder, "out"));
        File.WriteAllText(Path.Combine(folder, "out", "held.txt"), "x");
        return folder;
    }

    [Fact]
    public async Task AFolderStillInUseIsRemovedOnALaterTry()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete on Windows only.");
        var clock = new FakeTimeProvider();
        var remover = new WorkFolderRemover(clock, NullLogger<WorkFolderRemover>.Instance);
        var folder = Folder();
        var held = new FileStream(Path.Combine(folder, "out", "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        var removal = remover.RemoveAsync(folder, CancellationToken.None);
        removal.IsCompleted.ShouldBeFalse("the first try failed and waits");
        await held.DisposeAsync();
        clock.Advance(WorkFolderRemover.Pause);

        (await removal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).ShouldBeTrue();
        Directory.Exists(folder).ShouldBeFalse();
    }

    [Fact]
    public async Task AFolderThatStaysInUseIsLeftAfterTheLastTry()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete on Windows only.");
        var clock = new FakeTimeProvider();
        var remover = new WorkFolderRemover(clock, NullLogger<WorkFolderRemover>.Instance);
        var folder = Folder();
        await using (new FileStream(Path.Combine(folder, "out", "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var removal = remover.RemoveAsync(folder, CancellationToken.None);
            // Each try's pause starts on another thread after the one before: move the clock until the remover gives up.
            for (var i = 0; i < 500 && !removal.IsCompleted; i++)
            {
                clock.Advance(WorkFolderRemover.Pause);
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }

            (await removal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).ShouldBeFalse();
        }

        Directory.Exists(folder).ShouldBeTrue();
        (await remover.RemoveAsync(folder, CancellationToken.None)).ShouldBeTrue("free again: it goes at once");
        (await remover.RemoveAsync(folder, CancellationToken.None)).ShouldBeTrue("a folder that is not there is nothing to do");
    }
}

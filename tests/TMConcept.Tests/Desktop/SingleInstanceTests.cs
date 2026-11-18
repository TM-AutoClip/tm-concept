using Shouldly;
using TMConcept.Desktop.Instance;

namespace TMConcept.Tests.Desktop;

public sealed class SingleInstanceTests
{
    [Fact]
    public void TheLockFileOfTheDataFolderLetsOnlyOneInstanceBeFirst()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "instance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using (var first = SingleInstance.Acquire(folder))
            {
                first.IsFirst.ShouldBeTrue();
                using var second = SingleInstance.Acquire(folder);
                second.IsFirst.ShouldBeFalse();
                File.Exists(Path.Combine(folder, SingleInstance.LockFileName)).ShouldBeTrue();
            }

            // The lock goes with the first instance.
            using var again = SingleInstance.Acquire(folder);
            again.IsFirst.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ASecondStartWhileTheFirstIsClosingOpensOnceTheLockIsFree()
    {
        // "Install TM Concept": Setup opens the installed copy while the portable one still frees the graphics card at exit.
        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "instance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var first = SingleInstance.Acquire(folder);
            first.IsFirst.ShouldBeTrue();
            first.StopListening();
            using (var second = SingleInstance.Acquire(folder))
            {
                second.IsFirst.ShouldBeFalse();
                second.SignalFirst().ShouldBeFalse("a closing instance no longer answers, so the second start waits instead of ending");
            }

            var waiting = Task.Run(() => SingleInstance.WaitForClosing(folder, TimeSpan.FromSeconds(30)), TestContext.Current.CancellationToken);
            await Task.Delay(700, TestContext.Current.CancellationToken);
            waiting.IsCompleted.ShouldBeFalse("the lock is still held");
            first.Dispose();
            using var opened = await waiting.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            opened.IsFirst.ShouldBeTrue();

            using var late = SingleInstance.WaitForClosing(folder, TimeSpan.Zero);
            late.IsFirst.ShouldBeFalse("no wait: one try");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

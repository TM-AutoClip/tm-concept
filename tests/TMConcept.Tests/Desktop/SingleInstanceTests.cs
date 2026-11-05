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
}

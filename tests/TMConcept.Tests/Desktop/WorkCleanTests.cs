using Avalonia.Headless.XUnit;
using Shouldly;
using TMConcept.Core.Paths;
using TMConcept.Core.Videos;

namespace TMConcept.Tests.Desktop;

public sealed class WorkCleanTests
{
    [AvaloniaFact]
    public void AtStartAnOrphanedRunFolderIsEmptiedButARejectedCodeAndPaidSceneImagesStay()
    {
        using var h = new DesktopHarness();
        var work = h.Get<IAppPaths>().WorkDirectory;
        var orphan = Path.Combine(work, "videos", "orphan");
        var rejected = Path.Combine(work, "videos", "rejected");
        var images = Path.Combine(work, "scene-images", "concept");
        foreach (var folder in new[] { Path.Combine(orphan, "project"), Path.Combine(rejected, "project"), images })
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(Path.Combine(orphan, "silent.mp4"), "x");
        File.WriteAllText(Path.Combine(rejected, "rejected-1.txt"), "code");
        File.WriteAllText(Path.Combine(rejected, "silent.mp4"), "x");
        File.WriteAllText(Path.Combine(images, "1.png"), "x");

        h.Get<VideoPipeline>().CleanWork();

        Directory.Exists(orphan).ShouldBeFalse();
        Directory.EnumerateFileSystemEntries(rejected).Select(Path.GetFileName).ShouldBe(["rejected-1.txt"]);
        File.Exists(Path.Combine(images, "1.png")).ShouldBeTrue();
    }
}

using Shouldly;
using TMConcept.Core.Processes;

namespace TMConcept.Tests.Core;

/// <summary>A child program's output is summarized (first error lines), never dumped.</summary>
public sealed class ChildOutputTests
{
    [Fact]
    public void TheFirstErrorLinesAreKept()
    {
        var output = string.Join('\n', Enumerable.Range(1, 30).Select(i => "frame " + i)
            .Concat(["Error: Cannot find module 'x'", "    at load (node:internal)", "npm ERR! code 1", "done"]));
        ChildOutput.Summary(output).ShouldBe("Error: Cannot find module 'x' | npm ERR! code 1");
    }

    [Fact]
    public void WithoutErrorLinesTheLastLinesAreKeptAndTheTextIsCut()
    {
        ChildOutput.Summary("a\nb\nc\n").ShouldBe("b | c");
        ChildOutput.Summary(new string('x', 2000) + " failed").Length.ShouldBe(501);
        ChildOutput.Summary(null).ShouldBeEmpty();
    }

    [Fact]
    public void TheHomeFolderIsMaskedBeforeTheCutAndTheCutFallsBetweenWords()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var line = "Error: cannot read " + Path.Combine(home, "Documents", "TM Concept", "clip.mp4") + " now";
        var lines = Enumerable.Range(0, 4).Select(i => line + " #" + i).ToList();

        var summary = ChildOutput.Summary(string.Join('\n', lines), characters: 150);

        // The home folder is gone even where the cut falls, and the cut is at a space of the masked text.
        var masked = string.Join(" | ", lines).Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        summary.ShouldNotContain(home, Case.Insensitive);
        summary.ShouldEndWith("…");
        summary.Length.ShouldBeLessThanOrEqualTo(151);
        masked.ShouldStartWith(summary[..^1]);
        masked[summary.Length - 1].ShouldBe(' ');
    }

    [Fact]
    public void FfmpegPathsAreReplacedBeforeTheCut()
    {
        var folder = Path.Combine("D:" + Path.DirectorySeparatorChar + "Portable","library", "Byzantium Chronicles", "2026-11-06-1-Greek fire");
        var video = Path.Combine(folder, "video.mp4");
        var jpeg = Path.Combine(folder, "thumb.jpg");
        var output = string.Join('\n', Enumerable.Range(0, 6).Select(i => $"Error opening input {video} or output {Path.Combine(folder, "x" + i + ".gray")}: Invalid data"));

        var text = TMConcept.Engines.Media.FfmpegMedia.Paths(output, video, jpeg, folder);

        text.ShouldNotContain("Byzantium");
        text.ShouldNotContain("Portable");
        text.ShouldContain("video.mp4");
        text.ShouldContain("<video folder>");
    }
}

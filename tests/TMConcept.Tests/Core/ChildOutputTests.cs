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
}

using Shouldly;
using TMConcept.Core.Engines;

namespace TMConcept.Tests.Core;

public sealed class EngineProgressParserTests
{
    [Fact]
    public void ReadsHyperFramesProgress()
    {
        var e = EngineProgressParser.Parse("@hf-progress {\"code\":\"capture\",\"pct\":50,\"done\":10,\"total\":20}")!;
        e.Ratio.ShouldBe(0.5);
        e.Message.ShouldBe("capturing frames 10/20");
    }

    [Fact]
    public void ReadsScriptEvents()
    {
        EngineProgressParser.Parse("{\"event\":\"progress\",\"ratio\":0.5,\"message\":\"5/10\"}")!.Ratio.ShouldBe(0.55);
        EngineProgressParser.Parse("{\"event\":\"done\"}")!.Done.ShouldBeTrue();
        EngineProgressParser.Parse("{\"event\":\"error\",\"message\":\"boom\"}")!.Error.ShouldBe("boom");
        EngineProgressParser.Parse("{\"event\":\"cancel\"}")!.Canceled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("plain log line")]
    [InlineData("{not json")]
    [InlineData("{\"other\":1}")]
    public void OtherLinesAreIgnored(string line) => EngineProgressParser.Parse(line).ShouldBeNull();
}

using Shouldly;
using TMConcept.Core.Engines;

namespace TMConcept.Tests.Core;

public sealed class EngineCodeCheckTests
{
    private const string HyperFramesOk = "(function () {\n  const tl = gsap.timeline({ paused: true });\n  window.__timelines = window.__timelines || {};\n  window.__timelines[\"concept\"] = tl;\n})();\n";

    private const string RemotionOk = "import React from 'react';\nimport {AbsoluteFill} from 'remotion';\nimport type {Input} from './input';\nexport const Scene: React.FC<Input> = (g) => <AbsoluteFill />;\n";

    [Fact]
    public void CleanCodePasses()
    {
        EngineCodeCheck.Check(EngineKind.HyperFrames, HyperFramesOk).ShouldBeEmpty();
        EngineCodeCheck.Check(EngineKind.Remotion, RemotionOk).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("fetch('x');")]
    [InlineData("const s = localStorage;")]
    [InlineData("el.innerHTML = 'x';")]
    [InlineData("window['ev' + 'al']('1');")]
    [InlineData("const u = 'https://example.com';")]
    [InlineData("const t = '\\x41';")]
    [InlineData("new Function('return 1')();")]
    public void ForbiddenPatternsAreFound(string line) =>
        EngineCodeCheck.Check(EngineKind.HyperFrames, HyperFramesOk + line).ShouldNotBeEmpty();

    [Fact]
    public void ImportsOutsideTheAllowListAreFound()
    {
        var code = RemotionOk + "import secret from '../../data/settings.json';\n";
        EngineCodeCheck.Check(EngineKind.Remotion, code).ShouldContain(f => f.Contains("../../data/settings.json", StringComparison.Ordinal));
        EngineCodeCheck.Check(EngineKind.Remotion, "a; import x from/**/'fs';\n" + RemotionOk).ShouldNotBeEmpty();
    }

    [Fact]
    public void HyperFramesCodeHasNoModules() =>
        EngineCodeCheck.Check(EngineKind.HyperFrames, "export const a = 1;\n" + HyperFramesOk).ShouldNotBeEmpty();

    [Fact]
    public void MissingRequiredExportIsFound() =>
        EngineCodeCheck.Check(EngineKind.Revideo, "import {makeScene2D} from '@revideo/2d';\n").ShouldContain(f => f.StartsWith("Required", StringComparison.Ordinal));

    [Fact]
    public void SvgNamespaceIsAllowed() =>
        EngineCodeCheck.Check(EngineKind.HyperFrames, "document.createElementNS('" + EngineCodeCheck.SvgNamespace + "', 'svg');\n" + HyperFramesOk).ShouldBeEmpty();

    [Fact]
    public void ExtractCodeTakesTheFirstFence() =>
        EngineCodeCheck.ExtractCode("Sure:\n```js\nconst a = 1;\n```\nmore").ShouldBe("const a = 1;\n");
}

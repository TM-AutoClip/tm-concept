using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Engines;
using TMConcept.Core.Processes;
using TMConcept.Core.Videos;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Processes;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Engines;
using TMConcept.Tests.Storage;

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
    [InlineData("const t = '\\u0066etch';")]
    [InlineData("const t = '\\u{66}';")]
    [InlineData("window.location.href = 'x';")]
    [InlineData("location.assign(u);")]
    [InlineData("location = target;")]
    [InlineData("top.location = u;")]
    [InlineData("parent.postMessage(1);")]
    [InlineData("const m = await import('node:fs');")]
    public void ForbiddenPatternsAreFound(string line) =>
        EngineCodeCheck.Check(EngineKind.HyperFrames, HyperFramesOk + line).ShouldNotBeEmpty();

    [Theory]
    [InlineData("function pulse(node: HTMLElement) { return node; }")]
    [InlineData("const location = scene.visual;")]
    [InlineData("const x = pin.location.x + box.top.y + parent.width;")]
    [InlineData("let top = 0; top = top + 4;")]
    [InlineData("const sep = ' \\u2014 ' + '\\u00b7' + '\\xb7';")]
    public void OrdinaryDrawingCodePasses(string line) =>
        EngineCodeCheck.Check(EngineKind.HyperFrames, HyperFramesOk + line + "\n").ShouldBeEmpty();

    [Fact]
    public void ATypeScriptAnnotationNamedNodePassesInRevideo()
    {
        const string code = "import {makeScene2D, Rect} from '@revideo/2d';\nexport default makeScene2D('scene', function* (view) {\n  const grow = (node: Rect) => node.width(100, 1);\n});\n";
        EngineCodeCheck.Check(EngineKind.Revideo, code).ShouldBeEmpty();
    }

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
    // 0.1.10: a model that only read "no web addresses" built the namespace from pieces, then used DOMParser; both are refused,
    // so the prompt (system and repair) names the one whole literal that passes.
    [Fact]
    public void PiecedSvgNamespaceAndMarkupParsingAreRefused()
    {
        EngineCodeCheck.Check(EngineKind.HyperFrames, HyperFramesOk + "var NS = [\"http:\", \"\", \"www.w3.org\", \"2000\", \"svg\"].join(\"/\");").ShouldNotBeEmpty();
        EngineCodeCheck.Check(EngineKind.HyperFrames, HyperFramesOk + "var d = new DOMParser().parseFromString(\"<svg></svg>\", \"text/html\");").ShouldNotBeEmpty();
    }

    [Fact]
    public void RepairPromptNamesTheWholeSvgNamespace()
    {
        var text = VideoCodePrompt.Repair("x", "line 1: address");
        text.ShouldContain("createElementNS(\"" + EngineCodeCheck.SvgNamespace + "\"");
        text.ShouldContain("DOMParser");
        text.ShouldContain("comments included");
        VideoCodePrompt.Version.ShouldBe("10", "10: the checklist of a local code model");
    }

    [Theory]
    [InlineData("// no DOMParser here, only createElementNS")]
    [InlineData("/* never innerHTML */")]
    [InlineData("// see https://gsap.com for easing")]
    public void RefusedNamesAndAddressesInCommentsAreStillFound(string line) =>
        // Strict on purpose: comments are not skipped (telling them from strings needs a parser); the prompt asks for clean comments instead.
        EngineCodeCheck.Check(EngineKind.HyperFrames, HyperFramesOk + line + "\n").ShouldNotBeEmpty();

    [Fact]
    public void ALocalCodeModelGetsTheChecklistAndItsRepairNamesTheFixAndAFileThatPasses()
    {
        // 0.1.23, the developer's full local run (Qwen 3.5 4B): the whole file on one line with innerHTML, then a repair without the timeline.
        using var paths = new TempPaths();
        var engine = HyperFrames(paths);

        VideoCodePrompt.System(engine, true, null, "en", local: false).ShouldNotContain("Checklist");
        var local = VideoCodePrompt.System(engine, true, null, "en", local: true);
        local.ShouldContain("Checklist before you answer");
        local.ShouldContain("One statement per line");
        local.ShouldContain(EngineCodeCheck.RequiredExport(EngineKind.HyperFrames).Description);

        var findings = EngineCodeCheck.Check(EngineKind.HyperFrames, "(function(){const t={};document.body.innerHTML=\"<b>x</b>\";})();");
        findings.ShouldContain(f => f.Contains("HTML injection", StringComparison.Ordinal));
        findings.ShouldContain(f => f.StartsWith("Required structure missing", StringComparison.Ordinal));
        var repair = VideoCodePrompt.Repair(engine, "x", findings);
        repair.ShouldContain("Fix: create every element with document.createElement and set its text with element.textContent");
        repair.ShouldContain("Fix: write exactly that structure into the file");
        repair.ShouldContain(engine.SampleCode.Trim());
        EngineCodeCheck.Check(EngineKind.HyperFrames, engine.SampleCode).ShouldBeEmpty("the file the repair shows passes");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheAppsOwnHyperFramesCodePassesTheSafetyAndTemplateChecks(bool subtitles)
    {
        using var paths = new TempPaths();
        var engine = HyperFrames(paths);

        EngineCodeCheck.Check(EngineKind.HyperFrames, engine.FallbackCode).ShouldBeEmpty();
        var style = new TMConcept.Core.Styles.VideoStyle { Name = "t", Subtitles = subtitles };
        var result = TMConcept.Core.Styles.VideoStyleCheck.Check(engine.FallbackCode, style, "en");
        result.Findings.ShouldBeEmpty();
        result.Fixes.ShouldBeEmpty("no colour of its own: the template's or the theme's");
        engine.FallbackCode.ShouldNotBe(engine.SampleCode);
    }

    private static HyperFramesEngine HyperFrames(TempPaths paths)
    {
        IProcessRunner runner = new ProcessRunner(ChildProcessTests.Guard(), NullLogger<ProcessRunner>.Instance);
        var platform = new WindowsToolPlatform(runner);
        var tools = new ToolInstaller(paths, platform, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance),
            NullLogger<ToolInstaller>.Instance);
        var chrome = new ChromeLocator(paths, platform, NullLogger<ChromeLocator>.Instance);
        return new HyperFramesEngine(paths, tools, chrome, runner, NullLogger<HyperFramesEngine>.Instance);
    }

    [Fact]
    public void SystemPromptKeepsTheCodeSmallAndTheImageFirst()
    {
        using var paths = new TempPaths();
        IProcessRunner runner = new ProcessRunner(ChildProcessTests.Guard(), NullLogger<ProcessRunner>.Instance);
        var platform = new WindowsToolPlatform(runner);
        var tools = new ToolInstaller(paths, platform, new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance),
            NullLogger<ToolInstaller>.Instance);
        var chrome = new ChromeLocator(paths, platform, NullLogger<ChromeLocator>.Instance);
        var engine = new HyperFramesEngine(paths, tools, chrome, runner, NullLogger<HyperFramesEngine>.Instance);

        var text = VideoCodePrompt.System(engine, subtitles: true);

        text.ShouldContain("the image is the main visual");
        text.ShouldContain("at most about 10 simple elements per scene");
        text.ShouldContain("fractions of width and height");
        text.ShouldContain("one shared set of helpers");
        text.ShouldContain("under about 400 lines");
        text.ShouldContain("Do not plan exact geometry at length");
        text.ShouldContain("Never write these API names or any other web address (only the namespaces above are allowed)");
        text.ShouldNotContain("icons drawn with simple elements");
    }
}

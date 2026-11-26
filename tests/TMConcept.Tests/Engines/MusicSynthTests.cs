using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Engines.Components;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Music;
using TMConcept.Engines.Processes;
using TMConcept.Engines.Tools;
using TMConcept.Tests.Storage;

namespace TMConcept.Tests.Engines;

/// <summary>
/// 0.1.55, "Program composes": the synthesizer's npm packages are pinned (only registry.npmjs.org, each with its integrity) and embedded with the
/// app's render script, the SoundFont is pinned by host, size and SHA-256 and its host is allowed, the two components come after Node.js and
/// say what they download, and the render script gets the files it plays. Nothing is downloaded and nothing runs here.
/// </summary>
public sealed class MusicSynthTests
{
    [Fact]
    public void ThePackagesArePinnedAndEmbeddedWithTheRenderScript()
    {
        using var paths = new TempPaths();
        var package = new EnginePackage(MusicSynthRules.PackageName, paths, null!, null!, NullLogger.Instance);

        package.Files.Keys.ShouldBe(["package.json", "package-lock.json", "render.mjs"], ignoreOrder: true);
        package.Extract();
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(package.Root, "package.json")))!;
        manifest["dependencies"]!["spessasynth_core"]!.GetValue<string>().ShouldBe("4.4.10", "an exact version, never a range");
        var locked = JsonNode.Parse(File.ReadAllText(Path.Combine(package.Root, "package-lock.json")))!["packages"]!.AsObject()
            .Where(p => p.Key.Length > 0).ToList();
        locked.Select(p => p.Key).ShouldBe(["node_modules/fflate", "node_modules/spessasynth_core", "node_modules/stb-vorbis"], ignoreOrder: true);
        foreach (var (name, entry) in locked)
        {
            entry!["resolved"]!.GetValue<string>().ShouldStartWith("https://registry.npmjs.org/", Case.Sensitive, name);
            entry["integrity"]!.GetValue<string>().ShouldStartWith("sha512-", Case.Sensitive, name);
            entry["license"]!.GetValue<string>().ShouldBeOneOf("Apache-2.0", "MIT");
        }

        File.ReadAllText(Path.Combine(package.Root, "render.mjs")).ShouldContain("from \"spessasynth_core\"");
    }

    [Fact]
    public void TheSoundFontIsPinnedAndItsHostIsAllowed()
    {
        SoundFontPins.Archive.Url.Host.ShouldBe(SoundFontPins.Host);
        SoundFontPins.Archive.Url.Scheme.ShouldBe(Uri.UriSchemeHttps);
        SoundFontPins.Archive.Sha256.Length.ShouldBe(64);
        SoundFontPins.Archive.Sha256.ShouldAllBe(c => Uri.IsHexDigit(c) && !char.IsUpper(c));
        SoundFontPins.Archive.ApproximateBytes.ShouldBe(SoundFontPins.Bytes);
        SoundFontPins.Archive.Tool.ShouldBe(ToolKind.SoundFont);
        ToolManifest.AllowedHosts.ShouldContain(SoundFontPins.Host);
        SoundFontPins.LicenseNotice.ShouldContain("Frank Wen");
        SoundFontPins.LicenseNotice.ShouldContain("MIT");
    }

    [Fact]
    public void TheComponentsComeAfterNodeAndSayWhatTheyDownload()
    {
        using var paths = new TempPaths();
        var (renderer, store) = Synth(paths);
        var components = new MusicSynthComponentSource(renderer, store).Components;

        var synth = components.Single(c => c.Code == ComponentCodes.MusicSynth);
        var font = components.Single(c => c.Code == ComponentCodes.MusicSoundFont);

        synth.DependsOn.ShouldBe([ComponentCodes.Node]);
        synth.Ready.ShouldBeFalse();
        synth.Downloads().Single().Host.ShouldBe(EngineDownloads.NpmHost);
        synth.Folders().ShouldBe([Path.Combine(paths.ToolsDirectory, "engines", MusicSynthRules.PackageName)]);
        font.DependsOn.ShouldBeEmpty();
        font.Ready.ShouldBeFalse();
        font.Downloads().Single().ShouldBe(new ComponentDownload(font.Name, SoundFontPins.Host, SoundFontPins.Bytes));
        store.File.ShouldBe(Path.Combine(paths.ToolsDirectory, MusicSynthRules.SoundFontFolder, SoundFontPins.Version, SoundFontPins.FileName));
        renderer.Ready.ShouldBeFalse();

        // A file of the wrong size (a part that was never finished) is not the SoundFont.
        Directory.CreateDirectory(store.Folder);
        File.WriteAllBytes(store.File, [1, 2, 3]);
        store.Ready.ShouldBeFalse();
    }

    [Fact]
    public void TheRenderScriptGetsTheFilesItPlays()
    {
        SpessaSynthRenderer.Arguments("render.mjs", "a.sf3", "b.mid", "c.wav")
            .ShouldBe(["render.mjs", "--soundfont", "a.sf3", "--midi", "b.mid", "--out", "c.wav", "--rate", "44100", "--tail", "2.5"]);
    }

    internal static (SpessaSynthRenderer Renderer, SoundFontStore Store) Synth(TempPaths paths)
    {
        var runner = new ProcessRunner(ChildProcessTests.Guard(), NullLogger<ProcessRunner>.Instance);
        IToolPlatform platform = OperatingSystem.IsWindows() ? new WindowsToolPlatform(runner) : new LinuxToolPlatform(runner);
        var downloader = new ToolDownloader(DownloadTransport.Network, NullLogger<ToolDownloader>.Instance);
        var tools = new ToolInstaller(paths, platform, downloader, NullLogger<ToolInstaller>.Instance);
        var store = new SoundFontStore(paths, downloader, NullLogger<SoundFontStore>.Instance);
        return (new SpessaSynthRenderer(paths, tools, runner, store, NullLogger<SpessaSynthRenderer>.Instance), store);
    }
}

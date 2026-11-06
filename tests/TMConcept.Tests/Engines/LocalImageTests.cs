using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Models;
using TMConcept.Engines.Images;
using TMConcept.Engines.Tools;
using TMConcept.Providers.Catalog;

namespace TMConcept.Tests.Engines;

/// <summary>The local image models: pins, catalog entries and the sd-cli command line (nothing is run, no graphics card is used).</summary>
public sealed class LocalImageTests
{
    [Fact]
    public void EveryLocalImageModelIsInTheCatalogAndPinsItsWeights()
    {
        var catalog = new EmbeddedModelCatalog();
        foreach (var model in LocalImageModels.All)
        {
            var entry = catalog.Find(ModelProviders.Local, model.Id).ShouldNotBeNull(model.Id);
            entry.Capabilities.ShouldBe(ModelCapability.Image);
            entry.Requires.ShouldBe([ComponentCodes.ImagePrefix + model.Id]);
            entry.MinVideoMemoryGb.ShouldBeGreaterThan(0);
            model.Files.Count.ShouldBe(3);
            model.Files.ShouldAllBe(f => f.Url.Host == "huggingface.co" && f.Sha256.Length == 64 && f.ApproximateBytes > 100_000_000);
        }

        ToolManifest.Archives.Where(a => a.Tool == TMConcept.Core.Tools.ToolKind.ImageRuntime).Select(a => a.Rid).ShouldBe(["win-x64", "linux-x64"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(1080, 1920, 576, 1024)]
    [InlineData(1920, 1080, 1024, 576)]
    [InlineData(512, 512, 512, 512)]
    public void PicturesAreAtMost1024OnTheLongSideInStepsOf64(int width, int height, int expectedWidth, int expectedHeight) =>
        SdCppImageEngine.Size(width, height).ShouldBe((expectedWidth, expectedHeight));

    [Fact]
    public void TheCommandLineIsThePrototypes()
    {
        var model = LocalImageModels.ZImageTurbo;

        var arguments = SdCppImageEngine.Arguments(model, "a map of the city", 576, 1024, 42, "out.png", f => "/w/" + f.ExecutablePath);

        string After(string flag) => arguments[arguments.ToList().IndexOf(flag) + 1];
        After("--diffusion-model").ShouldBe("/w/z_image_turbo-Q8_0.gguf");
        After("--llm").ShouldBe("/w/Qwen3-4B-Instruct-2507-Q4_K_M.gguf");
        After("--vae").ShouldBe("/w/ae.safetensors");
        After("--steps").ShouldBe("8");
        After("--cfg-scale").ShouldBe("1.0");
        After("--sampling-method").ShouldBe("euler");
        After("-p").ShouldBe("a map of the city");
        After("-W").ShouldBe("576");
        After("-H").ShouldBe("1024");
        After("--max-vram").ShouldBe("4");
        arguments.ShouldContain("--diffusion-fa");
        arguments.ShouldContain("--vae-tiling");
    }
}

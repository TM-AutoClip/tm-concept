using Shouldly;
using TMConcept.Core.Components;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Engines.Images;
using TMConcept.Engines.Tools;
using TMConcept.Providers.Catalog;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The local image models: pins, catalog entries (class, license), the sd-cli command line of every family and the memory plan from the
/// free graphics memory (nothing is run, no graphics card is used).
/// </summary>
public sealed class LocalImageTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void EveryLocalImageModelIsInTheCatalogWithItsClassAndLicenseAndPinsItsWeights()
    {
        var catalog = new EmbeddedModelCatalog();
        LocalImageModels.All.Select(m => m.Id).ShouldBeUnique();
        catalog.With(ModelCapability.Image).Where(m => m.Provider == ModelProviders.Local).Select(m => m.Id)
            .ShouldBe(LocalImageModels.All.Select(m => m.Id), ignoreOrder: true);
        foreach (var model in LocalImageModels.All)
        {
            var entry = catalog.Find(ModelProviders.Local, model.Id).ShouldNotBeNull(model.Id);
            entry.Name.ShouldBe(model.Name);
            entry.Capabilities.ShouldBe(ModelCapability.Image);
            entry.Requires.ShouldBe([ComponentCodes.ImagePrefix + model.Id]);
            entry.MinVideoMemoryGb.ShouldBe(VramTiers.Gigabytes(model.Tier), model.Id);
            VramTiers.All.ShouldContain(model.Tier);
            entry.License.ShouldBe(model.License, model.Id);
            entry.LicenseUse.ShouldBe(model.LicenseUse, model.Id);
            entry.LicenseUrl.ShouldBe(model.LicenseUrl, model.Id);
            (model.LicenseUse == LicenseUse.Commercial || model.LicenseUrl.StartsWith("https://huggingface.co/", StringComparison.Ordinal))
                .ShouldBeTrue(model.Id + ": a license with conditions links its text");

            model.Weights.Count(w => w.Role is ImageFileRole.Diffusion or ImageFileRole.Full).ShouldBe(1, model.Id);
            model.Weights.Select(w => w.Role).ShouldBeUnique(model.Id);
            model.Files.Select(f => f.ExecutablePath).ShouldBeUnique(model.Id + ": the files share one folder");
            model.Files.ShouldAllBe(f => f.Url.Host == "huggingface.co" && f.Sha256.Length == 64 && f.ApproximateBytes > 100_000_000);
            // Pinned to a commit, never to a branch: a file uploaded again on main would fail its SHA-256 for good.
            model.Files.ShouldAllBe(f => System.Text.RegularExpressions.Regex.IsMatch(f.Url.AbsolutePath, "/resolve/[0-9a-f]{40}/"));
            model.Steps.ShouldBeGreaterThan(0);
            (model.CfgScale <= 1 || model.NegativePrompt.Length > 0).ShouldBeTrue(model.Id + ": a guided model has a negative prompt");
        }

        // The FLUX.2 VAE is the Apache-2.0 copy, never the one of FLUX.2 dev (non-commercial).
        LocalImageModels.All.SelectMany(m => m.Files).ShouldNotContain(f => f.Url.AbsolutePath.Contains("flux2-dev", StringComparison.OrdinalIgnoreCase));

        // The CUDA build is Windows only; the Vulkan and processor builds are on both systems.
        ToolManifest.Archives.Where(a => a.Tool == TMConcept.Core.Tools.ToolKind.ImageRuntime).Select(a => a.Rid).ShouldBe(["win-x64"]);
        ToolManifest.Archives.Where(a => a.Tool == TMConcept.Core.Tools.ToolKind.ImageRuntimeVulkan).Select(a => a.Rid).ShouldBe(["win-x64", "linux-x64"], ignoreOrder: true);
        ToolManifest.Archives.Where(a => a.Tool == TMConcept.Core.Tools.ToolKind.ImageRuntimeCpu).Select(a => a.Rid).ShouldBe(["win-x64", "linux-x64"], ignoreOrder: true);
    }

    [Fact]
    public void EveryMemoryClassHasModelsAndTheNonCommercialOnesAreMarked()
    {
        foreach (var tier in VramTiers.All)
        {
            LocalImageModels.All.ShouldContain(m => m.Tier == tier, $"the {(int)tier} GB class has a model");
        }

        LocalImageModels.All.Where(m => m.LicenseUse == LicenseUse.NonCommercial).Select(m => m.Id).ShouldBe(["flux2-klein-9b", "flux2-klein-9b-q8"], ignoreOrder: true);
        LocalImageModels.Flux3Klein.LicenseUse.ShouldBe(LicenseUse.Limited);
        LocalImageModels.Sd35Medium.LicenseUse.ShouldBe(LicenseUse.Limited);
        LocalImageModels.ZImageTurbo.LicenseUse.ShouldBe(LicenseUse.Commercial);
        LocalImageModels.Flux3Klein.MinSdCppBuild.ShouldBe(LocalImageModels.Flux3Build);

        // Full quality BF16 Z-Image is a 24 GB model; 12–16 GB cards get the Q8 one with the Q8 text encoder.
        LocalImageModels.ZImageTurboFull.Tier.ShouldBe(VramTier.Gb24);
        LocalImageModels.ZImageTurboFull.TotalBytes.ShouldBe(12_309_866_400 + 8_044_982_048 + 335_304_388);
        LocalImageModels.ZImageTurboQ8.Tier.ShouldBe(VramTier.Gb12);
        LocalImageModels.ZImageTurboQ8.Files.Select(f => f.ExecutablePath).ShouldBe(["z_image_turbo-Q8_0.gguf", "Qwen3-4B-Instruct-2507-Q8_0.gguf", "ae.safetensors"]);
    }

    [Theory]
    [InlineData(VramTier.Gb4, "z-image-turbo-q3,flux2-klein-4b,sdxl-lightning")]
    [InlineData(VramTier.Gb8, "z-image-turbo-q3,flux2-klein-4b,sdxl-lightning,z-image-turbo,flux3-klein,sd35-medium,sdxl-lightning-q8,flux2-klein-9b")]
    public void ACardSeesTheModelsOfItsOwnClassAndEveryLowerOne(VramTier card, string ids) =>
        LocalImageModels.All.Where(m => VramTiers.Sees(card, m.Tier)).Select(m => m.Id).ShouldBe(ids.Split(','));

    [Fact]
    public void AFifteenPointEightGigabyteCardSeesTheSixteenGigabyteModelsButNotTheLargerOnes()
    {
        var card = VramTiers.ForCard((long)(15.8 * GiB));

        LocalImageModels.All.Where(m => VramTiers.Sees(card, m.Tier)).ShouldContain(LocalImageModels.QwenImage);
        LocalImageModels.All.Where(m => !VramTiers.Sees(card, m.Tier)).Select(m => m.Id)
            .ShouldBe(["z-image-turbo-full", "flux2-klein-9b-q8", "qwen-image-q6", "qwen-image-q8"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(1080, 1920, 0, 576, 1024)]
    [InlineData(1920, 1080, 0, 1024, 576)]
    [InlineData(512, 512, 0, 512, 512)]
    [InlineData(1024, 1024, 1_048_576, 1024, 1024)]
    [InlineData(2048, 2048, 1_048_576, 1024, 1024)]
    [InlineData(1080, 1080, 500_000, 704, 704)]
    public void PicturesAreAtMost1024OnTheLongSideAndTheModelsPixelsInStepsOf64(int width, int height, long maxPixels, int expectedWidth, int expectedHeight)
    {
        var (w, h) = SdCppImageEngine.Size(width, height, maxPixels);

        (w, h).ShouldBe((expectedWidth, expectedHeight));
        (maxPixels == 0 || (long)w * h <= maxPixels).ShouldBeTrue();
    }

    [Fact]
    public void ZImageAndFluxGetTheirFilesStepsAndNoNegativePrompt()
    {
        var arguments = Arguments(LocalImageModels.ZImageTurbo, "a map of the city", "never shown");

        After(arguments, "--diffusion-model").ShouldBe("/w/z_image_turbo-Q8_0.gguf");
        After(arguments, "--llm").ShouldBe("/w/Qwen3-4B-Instruct-2507-Q4_K_M.gguf");
        After(arguments, "--vae").ShouldBe("/w/ae.safetensors");
        After(arguments, "--steps").ShouldBe("8");
        After(arguments, "--cfg-scale").ShouldBe("1.0");
        After(arguments, "--sampling-method").ShouldBe("euler");
        After(arguments, "-p").ShouldBe("a map of the city");
        After(arguments, "-W").ShouldBe("576");
        After(arguments, "-H").ShouldBe("1024");
        After(arguments, "--flash-attn").ShouldBe("auto,diffusion=on");
        arguments.ShouldNotContain("-n", "a distilled model (cfg 1) ignores the negative prompt");
        arguments.ShouldNotContain("--scheduler");
        arguments.ShouldNotContain("--flow-shift");

        var flux3 = Arguments(LocalImageModels.Flux3Klein, "a cat", null);
        After(flux3, "--llm").ShouldBe("/w/Qwen3VL-4B-Instruct-Q4_K_M.gguf");
        After(flux3, "--vae").ShouldBe("/w/flux2-vae.safetensors");
        After(flux3, "--steps").ShouldBe("16");

        After(Arguments(LocalImageModels.Flux2Klein, "a cat", null), "--steps").ShouldBe("4");
    }

    [Fact]
    public void StableDiffusionThreePointFiveGetsItsCheckpointThreeTextEncodersGuidanceAndNegativePrompt()
    {
        var arguments = Arguments(LocalImageModels.Sd35Medium, "a lighthouse", null);

        After(arguments, "-m").ShouldBe("/w/sd3.5_medium-Q4_0.gguf");
        After(arguments, "--clip_l").ShouldBe("/w/clip_l-Q8_0.gguf");
        After(arguments, "--clip_g").ShouldBe("/w/clip_g-Q8_0.gguf");
        After(arguments, "--t5xxl").ShouldBe("/w/t5xxl-Q4_0.gguf");
        arguments.ShouldNotContain("--diffusion-model");
        arguments.ShouldNotContain("--vae", "the VAE is in the checkpoint");
        After(arguments, "--cfg-scale").ShouldBe("4.5");
        After(arguments, "--steps").ShouldBe("28");
        After(arguments, "--slg-scale").ShouldBe("2.5");
        After(arguments, "-n").ShouldBe(LocalImageModels.DefaultNegativePrompt);

        After(Arguments(LocalImageModels.Sd35Medium, "a lighthouse", "people"), "-n").ShouldBe("people");
    }

    [Fact]
    public void SdxlLightningAndQwenImageGetTheirSchedulerAndFlowShift()
    {
        var sdxl = Arguments(LocalImageModels.SdxlLightning, "a fox", null);
        After(sdxl, "-m").ShouldBe("/w/sdxl_lightning_4step.q4_0.gguf");
        After(sdxl, "--vae").ShouldBe("/w/sdxl_vae.safetensors");
        After(sdxl, "--scheduler").ShouldBe("sgm_uniform");
        After(sdxl, "--steps").ShouldBe("4");
        sdxl.ShouldNotContain("-n");

        var qwen = Arguments(LocalImageModels.QwenImage, "a poster", null);
        After(qwen, "--diffusion-model").ShouldBe("/w/Qwen_Image-Q4_0.gguf");
        After(qwen, "--llm").ShouldBe("/w/Qwen2.5-VL-7B-Instruct.Q4_K_M.gguf");
        After(qwen, "--vae").ShouldBe("/w/qwen_image_vae.safetensors");
        After(qwen, "--cfg-scale").ShouldBe("2.5");
        After(qwen, "--flow-shift").ShouldBe("3.0");
        After(qwen, "--steps").ShouldBe("20");
        After(qwen, "-n").ShouldBe(LocalImageModels.DefaultNegativePrompt);
    }

    [Theory]
    // A 16 GB card with 13 GiB free holds Z-Image Q8 (6.6 GB) and its working memory: on the card, 1 GiB kept free.
    [InlineData("z-image-turbo-q8", 16, 13.0, true, 32.0, ImageMemoryLevel.Card, "--vae-tiling --max-vram -1")]
    // The 12.3 GB BF16 model on the same card does not fit; main memory holds its 20.7 GB: offloaded.
    [InlineData("z-image-turbo-full", 16, 13.0, true, 32.0, ImageMemoryLevel.Offload, "--vae-tiling --max-vram -1 --offload-to-cpu")]
    // A 6 GB card with 4.5 GiB free: 3.5 GiB after the 1 GiB reserve is less than the 2.3 GiB model and 2.5 GiB working memory: offloaded.
    [InlineData("flux2-klein-4b", 6, 4.5, true, 16.0, ImageMemoryLevel.Offload, "--vae-tiling --max-vram -1 --offload-to-cpu")]
    // A 32 GB card, 30 GiB free: 2 GiB kept free (size / 16).
    [InlineData("qwen-image-q8", 32, 30.0, true, 64.0, ImageMemoryLevel.Card, "--vae-tiling --max-vram -2")]
    // An estimated reading keeps an eighth: 24 GB card, estimate 20.5 GiB, 3 GiB kept.
    [InlineData("qwen-image-q6", 24, 20.5, false, 64.0, ImageMemoryLevel.Offload, "--vae-tiling --max-vram -3 --offload-to-cpu")]
    // Too little to work in at all: refused.
    [InlineData("flux2-klein-4b", 4, 2.0, true, 16.0, ImageMemoryLevel.Refused, "")]
    // The card cannot hold it and main memory is too small for the weights: refused.
    [InlineData("qwen-image", 16, 13.0, true, 8.0, ImageMemoryLevel.Refused, "")]
    public void TheMemoryPlanFollowsTheFreeMemory(string id, int cardGb, double freeGib, bool measured, double mainGib, ImageMemoryLevel level, string flags)
    {
        var model = LocalImageModels.All.Single(m => m.Id == id);
        var reading = new VramReading(cardGb * GiB, (long)(freeGib * GiB), measured, "test");

        var plan = ImageMemoryPlanner.Plan(model, ImageBackend.Vulkan, reading, (long)(mainGib * GiB));

        plan.Level.ShouldBe(level);
        string.Join(' ', plan.Flags).ShouldBe(flags);
        if (level == ImageMemoryLevel.Refused)
        {
            plan.Reason.ShouldContain(model.Name);
            plan.Reason.ShouldContain("lower memory class");
        }
    }

    [Fact]
    public void ALowBudgetOfThisAppAloneOffloadsButNeverRefuses()
    {
        var model = LocalImageModels.All.Single(m => m.Id == "flux2-klein-4b");

        // The window in the background: Windows lowered this app's budget to 2 GiB, while 12 GiB of the card are free across all processes.
        var budget = new VramReading(16 * GiB, 2 * GiB, true, "DXGI budget") { SystemFreeBytes = 12 * GiB };
        ImageMemoryPlanner.Plan(model, ImageBackend.Vulkan, budget, 32 * GiB).Level.ShouldBe(ImageMemoryLevel.Offload);

        // The same 2 GiB measured across all processes is a real shortage: refused.
        ImageMemoryPlanner.Plan(model, ImageBackend.Vulkan, new VramReading(16 * GiB, 2 * GiB, true, "PDH"), 32 * GiB).Level.ShouldBe(ImageMemoryLevel.Refused);
    }

    [Fact]
    public void TheProcessorBuildGetsNoMemoryOptionsAndAnUnknownCardIsOffloaded()
    {
        var processor = ImageMemoryPlanner.Plan(LocalImageModels.ZImageTurbo, ImageBackend.Cpu, VramReading.None, null);
        processor.Level.ShouldBe(ImageMemoryLevel.Processor);
        processor.Flags.ShouldBeEmpty();
        var unknown = ImageMemoryPlanner.Plan(LocalImageModels.ZImageTurbo, ImageBackend.Vulkan, VramReading.None, null);
        unknown.Level.ShouldBe(ImageMemoryLevel.Offload);
        unknown.Flags.ShouldBe(["--vae-tiling", "--max-vram", "-1", "--offload-to-cpu"]);

        // Unknown main memory is not held against a model.
        ImageMemoryPlanner.Plan(LocalImageModels.ZImageTurboFull, ImageBackend.Cuda, new VramReading(16 * GiB, 13 * GiB, true, "t"), null).Level
            .ShouldBe(ImageMemoryLevel.Offload);
    }

    [Fact]
    public void AnOutOfMemoryEndStepsDownOneLevelUntilTheTextEncoderRunsOnTheProcessor()
    {
        var card = ImageMemoryPlanner.Plan(LocalImageModels.ZImageTurbo, ImageBackend.Vulkan, new VramReading(16 * GiB, 13 * GiB, true, "t"), null);
        card.Level.ShouldBe(ImageMemoryLevel.Card);

        var offload = ImageMemoryPlanner.StepDown(card).ShouldNotBeNull();
        offload.Level.ShouldBe(ImageMemoryLevel.Offload);
        offload.Flags.ShouldBe(["--vae-tiling", "--max-vram", "-1", "--offload-to-cpu"]);
        var encoder = ImageMemoryPlanner.StepDown(offload).ShouldNotBeNull();
        encoder.Level.ShouldBe(ImageMemoryLevel.OffloadTextEncoder);
        encoder.Flags.ShouldBe(["--vae-tiling", "--max-vram", "-1", "--offload-to-cpu", "--backend", "te=cpu"]);
        ImageMemoryPlanner.StepDown(encoder).ShouldBeNull();

        ImageMemoryPlanner.IsOutOfMemory("ggml_vulkan: Device memory allocation of size 3154 failed: ErrorOutOfDeviceMemory").ShouldBeTrue();
        ImageMemoryPlanner.IsOutOfMemory("CUDA error: out of memory").ShouldBeTrue();
        ImageMemoryPlanner.IsOutOfMemory("model manager cannot make enough memory available on Vulkan0").ShouldBeTrue();
        ImageMemoryPlanner.IsOutOfMemory("failed to load model").ShouldBeFalse();
        ImageMemoryPlanner.IsOutOfMemory(string.Empty).ShouldBeFalse();
    }

    [Fact]
    public void FreeMainMemoryIsReadFromMemAvailableOnLinux()
    {
        TMConcept.Engines.Hardware.SystemMemory.Available(["MemTotal:       32768000 kB", "MemFree:         1000000 kB", "MemAvailable:   20000000 kB"])
            .ShouldBe(20_000_000L * 1024);
        TMConcept.Engines.Hardware.SystemMemory.Available(["MemTotal: 1 kB"]).ShouldBeNull();
        new TMConcept.Engines.Hardware.SystemMemory().AvailableBytes().ShouldNotBeNull().ShouldBeGreaterThan(0, "this computer's own figure");
    }

    private static IReadOnlyList<string> Arguments(LocalImageModel model, string prompt, string? negative) =>
        SdCppImageEngine.Arguments(model, prompt, negative, 576, 1024, 42, "out.png", f => "/w/" + f.ExecutablePath, []);

    private static string After(IReadOnlyList<string> arguments, string flag)
    {
        var index = arguments.ToList().IndexOf(flag);
        index.ShouldBeGreaterThanOrEqualTo(0, flag);
        return arguments[index + 1];
    }
}

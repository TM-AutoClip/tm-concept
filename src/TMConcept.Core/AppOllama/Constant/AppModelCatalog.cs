using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// The models the app offers for its own Ollama copy, and which ones it recommends for each graphics card class (0.1.20, the developer:
/// the Qwen 3.5, Gemma 4 and gpt-oss families; a 6 GB card should get at least a Gemma 4 model of the 4B class). Every tag is an exact tag of
/// the Ollama library (never <c>latest</c>, so the size is known); every size is the sum of the layers of that tag's registry manifest
/// (<c>https://registry.ollama.ai/v2/library/&lt;name&gt;/manifests/&lt;tag&gt;</c>), read on 17 Nov 2026 together with the library pages
/// <c>https://ollama.com/library/qwen3.5/tags</c>, <c>https://ollama.com/library/gemma4/tags</c> and <c>https://ollama.com/library/gpt-oss/tags</c>
/// (sizes, context windows, "Text, Image input"; capabilities "vision tools thinking" on each family page). Licenses: Apache-2.0 for all
/// three families (Hugging Face model cards of Qwen/Qwen3.5-9B, google/gemma-4-E4B-it and openai/gpt-oss-20b; gpt-oss's Ollama page says so
/// too). The KV cache figures are estimates from the models' configurations (hybrid attention keeps Qwen 3.5's small; Gemma 4 uses a sliding
/// window in most layers), so a card's fit is a guide, not a promise: Ollama itself moves what does not fit to main memory.
/// </summary>
public static class AppModelCatalog
{
    private const string Apache = "apache-2.0";
    private const string QwenLicense = "https://huggingface.co/Qwen/Qwen3.5-9B";
    private const string GemmaLicense = "https://huggingface.co/google/gemma-4-E4B-it";
    private const string GptOssLicense = "https://huggingface.co/openai/gpt-oss-20b";

    // https://ollama.com/library/qwen3.5/tags
    private const string QwenTags = "https://ollama.com/library/qwen3.5/tags";

    // https://ollama.com/library/gemma4/tags
    private const string GemmaTags = "https://ollama.com/library/gemma4/tags";

    // https://ollama.com/library/gpt-oss/tags
    private const string GptOssTags = "https://ollama.com/library/gpt-oss/tags";

    private const long Kb = 1024;

    /// <summary>Every model the app offers, smallest first.</summary>
    public static readonly IReadOnlyList<AppModelInfo> Models =
    [
        // qwen3.5:2b-q4_K_M: 1.9GB, 256K context, text + image input (manifest: 1 945 781 181 bytes).
        new("qwen3.5:2b-q4_K_M", "Qwen 3.5 2B", "qwen3.5", 1_945_781_181, 8_192, 262_144, 16 * Kb, true, true, Apache, LicenseUse.Commercial, QwenLicense, QwenTags),

        // qwen3.5:4b-q4_K_M: 3.3GB, 256K (manifest: 3 324 173 757 bytes).
        new("qwen3.5:4b-q4_K_M", "Qwen 3.5 4B", "qwen3.5", 3_324_173_757, 8_192, 262_144, 32 * Kb, true, true, Apache, LicenseUse.Commercial, QwenLicense, QwenTags),

        // gemma4:e2b-it-qat: 4.3GB, 128K (manifest: 4 336 357 640 bytes).
        new("gemma4:e2b-it-qat", "Gemma 4 E2B", "gemma4", 4_336_357_640, 8_192, 131_072, 64 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // qwen3.5:4b-q8_0: 5.2GB, 256K (manifest: 5 157 983 677 bytes).
        new("qwen3.5:4b-q8_0", "Qwen 3.5 4B (Q8)", "qwen3.5", 5_157_983_677, 8_192, 262_144, 32 * Kb, true, true, Apache, LicenseUse.Commercial, QwenLicense, QwenTags),

        // gemma4:e4b-it-qat: 6.1GB, 128K (manifest: 6 146 501 256 bytes).
        new("gemma4:e4b-it-qat", "Gemma 4 E4B", "gemma4", 6_146_501_256, 8_192, 131_072, 64 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // qwen3.5:9b-q4_K_M: 6.6GB, 256K (manifest: 6 550 825 373 bytes).
        new("qwen3.5:9b-q4_K_M", "Qwen 3.5 9B", "qwen3.5", 6_550_825_373, 16_384, 262_144, 32 * Kb, true, true, Apache, LicenseUse.Commercial, QwenLicense, QwenTags),

        // gemma4:12b-it-qat: 7.2GB, 256K (manifest: 7 151 003 208 bytes).
        new("gemma4:12b-it-qat", "Gemma 4 12B (QAT)", "gemma4", 7_151_003_208, 8_192, 262_144, 96 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // gemma4:12b-it-q4_K_M: 8.0GB, 256K (manifest: 8 021 618 699 bytes).
        new("gemma4:12b-it-q4_K_M", "Gemma 4 12B", "gemma4", 8_021_618_699, 16_384, 262_144, 96 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // qwen3.5:9b-q8_0: 10GB, 256K (manifest: 10 449 217 949 bytes).
        new("qwen3.5:9b-q8_0", "Qwen 3.5 9B (Q8)", "qwen3.5", 10_449_217_949, 16_384, 262_144, 32 * Kb, true, true, Apache, LicenseUse.Commercial, QwenLicense, QwenTags),

        // gemma4:12b-it-q8_0: 13GB, 256K (manifest: 12 844 771 528 bytes).
        new("gemma4:12b-it-q8_0", "Gemma 4 12B (Q8)", "gemma4", 12_844_771_528, 16_384, 262_144, 96 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // gpt-oss:20b: 14GB, 128K, text input only (manifest: 13 793 441 427 bytes).
        new("gpt-oss:20b", "gpt-oss 20B", "gpt-oss", 13_793_441_427, 16_384, 131_072, 32 * Kb, false, true, Apache, LicenseUse.Commercial, GptOssLicense, GptOssTags),

        // gemma4:26b-a4b-it-qat: 16GB, 256K, mixture of experts (manifest: 15 634 199 400 bytes).
        new("gemma4:26b-a4b-it-qat", "Gemma 4 26B A4B", "gemma4", 15_634_199_400, 16_384, 262_144, 128 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // qwen3.5:27b-q4_K_M: 17GB, 256K (manifest: 17 478 556 413 bytes).
        new("qwen3.5:27b-q4_K_M", "Qwen 3.5 27B", "qwen3.5", 17_478_556_413, 32_768, 262_144, 64 * Kb, true, true, Apache, LicenseUse.Commercial, QwenLicense, QwenTags),

        // gemma4:26b-a4b-it-q4_K_M: 18GB, 256K (manifest: 17 987 580 741 bytes).
        new("gemma4:26b-a4b-it-q4_K_M", "Gemma 4 26B A4B (Q4)", "gemma4", 17_987_580_741, 32_768, 262_144, 128 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // gemma4:31b-it-qat: 19GB, 256K (manifest: 18 851 735 688 bytes).
        new("gemma4:31b-it-qat", "Gemma 4 31B (QAT)", "gemma4", 18_851_735_688, 16_384, 262_144, 128 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // gemma4:31b-it-q4_K_M: 20GB, 256K (manifest: 20 385 457 992 bytes).
        new("gemma4:31b-it-q4_K_M", "Gemma 4 31B", "gemma4", 20_385_457_992, 32_768, 262_144, 128 * Kb, true, true, Apache, LicenseUse.Commercial, GemmaLicense, GemmaTags),

        // qwen3.5:35b-a3b-q4_K_M: 22GB, 256K, mixture of experts (manifest: 22 069 591 997 bytes).
        new("qwen3.5:35b-a3b-q4_K_M", "Qwen 3.5 35B A3B", "qwen3.5", 22_069_591_997, 32_768, 262_144, 48 * Kb, true, true, Apache, LicenseUse.Commercial, QwenLicense, QwenTags),
    ];

    /// <summary>
    /// The recommendations per card class, best of those that fit first. Every model fits a card of exactly its class with its context
    /// (<see cref="AppModelAdvisor.Fit"/>), with one deliberate exception: Gemma 4 E4B on a 6 GB card (the developer's expectation), listed
    /// last there and shown red (part runs in main memory). Gemma 4 26B A4B (QAT) needs about 17 GiB with its context, so it is a 24 GB model.
    /// </summary>
    public static readonly IReadOnlyDictionary<VramTier, IReadOnlyList<string>> Recommended = new Dictionary<VramTier, IReadOnlyList<string>>
    {
        [VramTier.None] = ["qwen3.5:2b-q4_K_M", "qwen3.5:4b-q4_K_M"],
        [VramTier.Gb4] = ["qwen3.5:4b-q4_K_M", "qwen3.5:2b-q4_K_M"],
        [VramTier.Gb6] = ["qwen3.5:4b-q8_0", "gemma4:e2b-it-qat", "gemma4:e4b-it-qat"],
        [VramTier.Gb8] = ["qwen3.5:9b-q4_K_M", "gemma4:e4b-it-qat", "gemma4:12b-it-qat"],
        [VramTier.Gb12] = ["gemma4:12b-it-q4_K_M", "qwen3.5:9b-q8_0", "qwen3.5:9b-q4_K_M"],
        [VramTier.Gb16] = ["gemma4:12b-it-q8_0", "gpt-oss:20b", "qwen3.5:9b-q8_0"],
        [VramTier.Gb24] = ["qwen3.5:27b-q4_K_M", "gemma4:31b-it-qat", "gemma4:26b-a4b-it-q4_K_M", "gemma4:26b-a4b-it-qat"],
        [VramTier.Gb32] = ["gemma4:31b-it-q4_K_M", "qwen3.5:35b-a3b-q4_K_M", "qwen3.5:27b-q4_K_M"],
    };
}

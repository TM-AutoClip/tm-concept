using TMConcept.Core.Models;

namespace TMConcept.Core.Pipeline;

/// <summary>
/// The steps that call a model. Each has its own choice: the general default (Settings → Models) and, per profile, an override (Profile
/// page); producing uses the profile's override, then the general default.
/// </summary>
public static class PipelineSteps
{
    /// <summary>Concept generation.</summary>
    public const string Concepts = "concepts";

    /// <summary>Video code and its single repair; the same model writes the image prompts (the engine is chosen in this step too).</summary>
    public const string VideoCode = "video-code";

    /// <summary>Scene images: off (the video is drawn by its code only), an online image model, or a local one.</summary>
    public const string Images = "images";

    /// <summary>Narration: off (silent video), the local voice, or an online voice.</summary>
    public const string Speech = "speech";

    /// <summary>Semantic duplicate check (optional).</summary>
    public const string Embedding = "embedding";

    /// <summary>Sample image description (optional).</summary>
    public const string DescribeImage = "describe-image";

    /// <summary>
    /// Cost label of the image prompts (not a step of its own: the video code model writes them). Kept apart from the code cost so the
    /// cost details show what the prompts cost.
    /// </summary>
    public const string ImagePromptCost = "image-prompt";

    /// <summary>Every step in display order.</summary>
    public static readonly IReadOnlyList<PipelineStep> All =
    [
        new(Concepts, ModelCapability.Chat, ["anthropic/claude-sonnet-5-5", "zai/glm-5.3", "anthropic/claude-haiku-5-5"], Optional: false),
        new(VideoCode, ModelCapability.Chat, ["anthropic/claude-sonnet-5-5", "anthropic/claude-opus-5-5"], Optional: false),
        new(Images, ModelCapability.Image, ["local/flux2-klein-4b", "local/z-image-turbo", "fal/fal-ai/flux-2/flash"], Optional: true),
        new(Speech, ModelCapability.Speech, ["local/supertonic"], Optional: true),
        new(Embedding, ModelCapability.Embedding, ["openai/text-embedding-3-small"], Optional: true),
        new(DescribeImage, ModelCapability.Vision, ["anthropic/claude-haiku-5-5"], Optional: true),
    ];
}

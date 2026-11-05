using TMConcept.Core.Models;

namespace TMConcept.Core.Pipeline;

/// <summary>The steps that call a model. Each has its own model choice (plan §4.2).</summary>
public static class PipelineSteps
{
    /// <summary>Concept generation.</summary>
    public const string Concepts = "concepts";

    /// <summary>Video code and its single repair.</summary>
    public const string VideoCode = "video-code";

    /// <summary>Image prompt writing.</summary>
    public const string ImagePrompt = "image-prompt";

    /// <summary>Image generation.</summary>
    public const string Image = "image";

    /// <summary>Online speech.</summary>
    public const string Speech = "speech";

    /// <summary>Semantic duplicate check.</summary>
    public const string Embedding = "embedding";

    /// <summary>Sample image description.</summary>
    public const string DescribeImage = "describe-image";

    /// <summary>Every step in display order.</summary>
    public static readonly IReadOnlyList<PipelineStep> All =
    [
        new(Concepts, ModelCapability.Chat, ["anthropic/claude-sonnet-5-5", "zai/glm-5.3", "anthropic/claude-haiku-5-5"]),
        new(VideoCode, ModelCapability.Chat, ["anthropic/claude-sonnet-5-5", "anthropic/claude-opus-5-5"]),
        new(ImagePrompt, ModelCapability.Chat, ["anthropic/claude-haiku-5-5", "zai/glm-5.3-flash"]),
        new(Image, ModelCapability.Image, ["fal/fal-ai/flux-2/flash"]),
        new(Speech, ModelCapability.Speech, []),
        new(Embedding, ModelCapability.Embedding, ["openai/text-embedding-3-small"]),
        new(DescribeImage, ModelCapability.Vision, ["anthropic/claude-haiku-5-5"]),
    ];
}

using TMConcept.Core.Models;

namespace TMConcept.Core.Pipeline;

/// <summary>
/// The steps that call a model. Each is chosen on the page that uses it (there is no model setting in Settings): Generate concepts =
/// concept model and the semantic duplicate check; Concept → video = video code model and engine, images, narration (and the concept model for
/// editing an approved concept there); Profile = describing
/// a sample image. The last choice is kept per profile and shown again next time.
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

    /// <summary>"Suggest topics" on the concept page: a cheap chat model writes 5-100 topics to pick from, one concept each (optional).</summary>
    public const string Topics = "topics";

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
        new(Speech, ModelCapability.Speech, ["local/supertonic", "local/kokoro"], Optional: true),
        new(Embedding, ModelCapability.Embedding, ["local/paraphrase-multilingual-minilm", "openai/text-embedding-3-small"], Optional: true),
        new(DescribeImage, ModelCapability.Vision, ["anthropic/claude-haiku-5-5"], Optional: true),
        new(Topics, ModelCapability.Chat, ["anthropic/claude-haiku-5-5", "zai/glm-5.3-flash", "deepseek/deepseek-flash"], Optional: true),
    ];

    /// <summary>The steps chosen on the Generate concepts page.</summary>
    public static readonly IReadOnlyList<string> ConceptPage = [Concepts, Embedding, Topics];

    /// <summary>
    /// The steps chosen on the Concept → video page: the video's own (code with its engine, images, narration) and the concept step, which
    /// the AI edit of an approved concept uses there. A video is made only with the first three (production names them itself).
    /// </summary>
    public static readonly IReadOnlyList<string> VideoPage = [VideoCode, Images, Speech, Concepts];

    /// <summary>The steps chosen on the Profile page (sample images).</summary>
    public static readonly IReadOnlyList<string> ProfilePage = [DescribeImage];
}

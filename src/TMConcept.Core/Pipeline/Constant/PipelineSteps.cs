using TMConcept.Core.Models;

namespace TMConcept.Core.Pipeline;

/// <summary>
/// The steps that call a model. Each is chosen on the page that uses it (there is no model setting in Settings): Generate concepts =
/// concept model and the semantic duplicate check; Concept → video = video code model and engine, images, narration (and the concept model for
/// editing an approved concept there). The last choice is kept per profile and shown again next time.
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

    /// <summary>
    /// Text extraction of scanned PDF pages while a document is added (optional, 0.1.24): a model that reads images, online (paid per page)
    /// or local.
    /// </summary>
    public const string DocumentText = "document-text";

    /// <summary>
    /// Cost label of the image prompts (not a step of its own: the video code model writes them). Kept apart from the code cost so the
    /// cost details show what the prompts cost.
    /// </summary>
    public const string ImagePromptCost = "image-prompt";

    /// <summary>
    /// Cost label of the scene plan of a video drawn by the app (0.1.33; not a step of its own: the video code model writes it instead of the
    /// code).
    /// </summary>
    public const string ScenePlanCost = "scene-plan";

    /// <summary>
    /// Where things are in the generated scene images (optional, 0.1.33): a model that reads images places the overlays of a video drawn by the
    /// app on the right spots (online and cheap, or a local one); off = the plan's own guesses.
    /// </summary>
    public const string ImagePoints = "image-points";

    /// <summary>
    /// Cost label of a template's generated background music (0.1.46, C5; not a step of its own: the template names the music model) and the
    /// step name its key is resolved under.
    /// </summary>
    public const string Music = "music";

    /// <summary>
    /// The quality check (optional, F11, 0.1.50): a model that reads images (online, or a local one such as Gemma 3 4B) reviews the rendered
    /// frames and the final critic scores the video when the template's quality mode is Standard or Careful; it can differ from the model that
    /// writes the plan. Off: the quality mode does nothing (a yellow warning). Its calls are this cost line.
    /// </summary>
    public const string QualityCheck = "quality-check";

    /// <summary>Cost label of the final critic's call (0.1.50; the quality check step's model makes it).</summary>
    public const string QualityCriticCost = "quality-critic";

    /// <summary>Cost label of the careful mode's repaired scene plans (0.1.50; the video code model writes them, one small call per scene).</summary>
    public const string QualityRepairCost = "quality-repair";

    /// <summary>Cost label of the careful mode's character sheet (F14, 0.1.50; one more picture of the image model).</summary>
    public const string CharacterSheetCost = "character-sheet";

    /// <summary>
    /// Not a model step (0.1.54): the name the profile's background music and reference video are kept under among its step choices
    /// (<see cref="Styles.VideoMediaStore"/>).
    /// </summary>
    public const string VideoMedia = "video-media";

    /// <summary>Every step in display order.</summary>
    public static readonly IReadOnlyList<PipelineStep> All =
    [
        new(Concepts, ModelCapability.Chat, ["anthropic/claude-sonnet-5-5", "zai/glm-5.3", "anthropic/claude-haiku-5-5"], Optional: false),
        new(VideoCode, ModelCapability.Chat, ["anthropic/claude-sonnet-5-5", "anthropic/claude-opus-5-5"], Optional: false),
        new(Images, ModelCapability.Image, ["local/flux2-klein-4b", "local/z-image-turbo", "fal/fal-ai/flux-2/flash"], Optional: true),
        new(Speech, ModelCapability.Speech, ["local/supertonic", "local/kokoro"], Optional: true),
        new(Embedding, ModelCapability.Embedding, ["local/paraphrase-multilingual-minilm", "openai/text-embedding-3-small"], Optional: true),
        new(Topics, ModelCapability.Chat, ["anthropic/claude-haiku-5-5", "zai/glm-5.3-flash", "deepseek/deepseek-flash"], Optional: true),
        new(DocumentText, ModelCapability.Vision, ["anthropic/claude-haiku-5-5"], Optional: true),
        new(ImagePoints, ModelCapability.Vision, ["app-ollama/gemma3:4b", "anthropic/claude-haiku-5-5"], Optional: true),
        new(QualityCheck, ModelCapability.Vision, ["app-ollama/gemma3:4b", "anthropic/claude-haiku-5-5"], Optional: true),
    ];

    /// <summary>The steps chosen on the Generate concepts page.</summary>
    public static readonly IReadOnlyList<string> ConceptPage = [Concepts, Embedding, Topics, DocumentText];

    /// <summary>
    /// The steps chosen on the Concept → video page: the video's own (code with its engine, images, the image points of
    /// a video drawn by the app, the quality check (0.1.50), narration) and the concept step, which the AI edit of an approved concept uses there (production names the
    /// video's own itself).
    /// </summary>
    public static readonly IReadOnlyList<string> VideoPage = [VideoCode, Images, ImagePoints, QualityCheck, Speech, Concepts];
}

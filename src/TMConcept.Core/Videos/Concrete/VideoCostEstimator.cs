using TMConcept.Core.Concepts;
using TMConcept.Core.Costs;
using TMConcept.Core.Engines;
using TMConcept.Core.Images;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Render;
using TMConcept.Core.Scenes;

namespace TMConcept.Core.Videos;

/// <summary>
/// The estimate of making videos before anything is paid for, with the models chosen on the page: the video code call (its real prompt is
/// built from the concept's scenes; the answer is a range from <see cref="CodeOutputMin"/> tokens to the whole output budget of the call,
/// <see cref="VideoCodeBudget.For"/>, and one repair may double it: the high end counts it), the image prompts and one image per scene when an image model is chosen, and the narration's
/// characters for an online voice, and (0.1.46, C5) a template's generated music: one piece per video at the music model's price
/// (<see cref="Music.MusicPricing"/>; a shorter piece is looped, never a second one made). Local models cost nothing. The exact cost of a made
/// video is its own record. <paramref name="catalog"/> finds a template's music model (none = its music is not counted).
/// </summary>
public sealed class VideoCostEstimator(ICostCalculator costs, IModelCatalog? catalog = null)
{
    /// <summary>Fewest output tokens of a video code answer (a short HyperFrames scene file).</summary>
    public const long CodeOutputMin = 3_000;

    /// <summary>Output tokens of the image prompt answer per scene.</summary>
    public const long PromptTokensPerScene = 120;

    /// <summary>
    /// The estimate of the given concepts; <paramref name="engine"/> null = the code prompt without the engine's own rules. With a template
    /// (<paramref name="style"/>) its prompt block is counted, its subtitles choice replaces <paramref name="subtitles"/>, a spoken closing adds
    /// its sentence to the narration and its drawing style goes into the image prompt.
    /// </summary>
    public CostEstimate Estimate(IReadOnlyList<Concept> concepts, Profile profile, IVideoEngine? engine, CatalogModel? code, CatalogModel? image,
        CatalogModel? speech, bool subtitles, Styles.VideoStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        ArgumentNullException.ThrowIfNull(profile);
        var total = CostEstimate.Zero;
        var (width, height) = RenderInputBuilder.Size(profile.Format);
        foreach (var concept in concepts)
        {
            var scenes = SceneSplitter.Split(concept.Content).Scenes;
            if (scenes.Count == 0)
            {
                continue;
            }

            var closing = Styles.VideoStyleRules.Closing(style, profile.ContentLanguage);
            var spoken = closing is { Voice: true } ? Styles.ClosingNarration.Append(scenes, closing.Sentence) : scenes;

            if (code is not null)
            {
                var input = RenderInputBuilder.Build(concept.Title, profile.ContentLanguage, spoken.Select(s => new RenderSceneSource(s.Visual, s.Narration,
                    string.Empty, VideoRemaker.SilentMs(s.Script, profile.ContentLanguage), s.Transition)).ToList(), width, height, 30,
                    endHoldMs: Styles.VideoStyleRules.EndHoldMs(style, profile.ContentLanguage));
                total = total.Plus(Code(input, profile.ContentLanguage, engine, code, subtitles, style));
                if (engine is not null && Plans.PlanDrawer.Draws(engine.Kind, style) && style!.PlanReview)
                {
                    // F10 (0.1.49): notes on a previewed plan cost one more plan call (all the notes together): at the high end only.
                    var again = Call(input, profile.ContentLanguage, engine, code, subtitles, style);
                    total = total.Plus(new CostEstimate(0, again.MaxUsd, 0, 0, 0));
                }
                if (image is not null)
                {
                    var visuals = scenes.Select(s => s.Visual.Length > 0 ? s.Visual : s.Narration).ToList();
                    var prompt = costs.Tokens(ImagePromptBuilder.System + ImagePromptBuilder.User(profile.Name, visuals, style?.DrawStyle), profile.ContentLanguage);
                    total = total.Plus(costs.EstimateChat(code, prompt, 0, PromptTokensPerScene * scenes.Count / 2, PromptTokensPerScene * scenes.Count));
                }
            }

            if (image is { Provider: not ModelProviders.Local })
            {
                var images = image.ImageUsd * scenes.Count;
                total = total.Plus(new CostEstimate(images, images, 0, 0, 0));
            }

            if (speech is { Provider: not ModelProviders.Local })
            {
                var voice = Speech(speech, spoken.Sum(s => (long)s.Narration.Length));
                total = total.Plus(new CostEstimate(voice, voice, 0, 0, 0));
            }

            if (MusicModel(style) is { } music)
            {
                var seconds = spoken.Sum(s => VideoRemaker.SilentMs(s.Script, profile.ContentLanguage)) / 1000.0;
                var usd = Music(music, seconds);
                total = total.Plus(new CostEstimate(usd, usd, 0, 0, 0));
            }
        }

        return total;
    }

    /// <summary>What a video of <paramref name="videoSeconds"/> pays for its generated music with <paramref name="music"/> (a local model nothing).</summary>
    public static decimal Music(CatalogModel music, double videoSeconds)
    {
        ArgumentNullException.ThrowIfNull(music);
        return Core.Music.MusicPricing.Usd(music, Core.Music.MusicPricing.Seconds(music, videoSeconds));
    }

    /// <summary>The music model of a template whose music is generated (null otherwise, or when the catalog does not know it).</summary>
    private CatalogModel? MusicModel(Styles.VideoStyle? style) =>
        style?.Music is { Source: Styles.MusicSource.GenerateLocal or Styles.MusicSource.GenerateOnline, Generator: { Length: > 0 } key }
        && key.Split('/', 2) is [{ } provider, { } id] && catalog?.Find(provider, id) is { } model && model.Capabilities.HasFlag(ModelCapability.Music)
            ? model
            : null;

    /// <summary>
    /// The video code call for <paramref name="input"/> (its real prompt): from <see cref="CodeOutputMin"/> output tokens to the call's whole
    /// budget, plus one repair at the high end. Also the estimate of a new version with another engine (same input, new code). With a template
    /// (<paramref name="style"/>) the prompt is the one the video gets: its block counted, its subtitles choice used.
    /// </summary>
    public CostEstimate Code(RenderInput input, string language, IVideoEngine? engine, CatalogModel code, bool subtitles, Styles.VideoStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(code);
        subtitles = style?.Subtitles ?? subtitles;
        var (system, user) = Prompt(input, language, engine, subtitles, style);

        // The high end is the call's whole budget: 14 000 tokens used to be counted, but real answers with thinking were cut at 32 000.
        long most = VideoCodeBudget.For(code);
        var first = costs.EstimateChat(code, system + user, 0, CodeOutputMin, most);

        // One repair may follow a rejected check: its cost is the high end only.
        var repair = costs.EstimateChat(code, system + user + most, system, 0, most);
        return first with { MaxUsd = first.MaxUsd + repair.MaxUsd };
    }

    /// <summary>One code (or plan) call for <paramref name="input"/> without its repair: from <see cref="CodeOutputMin"/> output tokens to the whole budget.</summary>
    public CostEstimate Call(RenderInput input, string language, IVideoEngine? engine, CatalogModel code, bool subtitles, Styles.VideoStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(code);
        var (system, user) = Prompt(input, language, engine, subtitles, style);
        return costs.EstimateChat(code, system + user, 0, CodeOutputMin, VideoCodeBudget.For(code));
    }

    /// <summary>The tokens of the code prompt's system and user parts.</summary>
    private (long System, long User) Prompt(RenderInput input, string language, IVideoEngine? engine, bool subtitles, Styles.VideoStyle? style)
    {
        subtitles = style?.Subtitles ?? subtitles;
        var system = engine is null ? 0 : costs.Tokens(VideoCodePrompt.System(engine, subtitles, style, language), ContentLanguages.English);
        var user = costs.Tokens(VideoCodePrompt.User(Render.RenderInputBuilder.ForEngine(input, subtitles)), language);
        return (system, user);
    }

    /// <summary>What an online voice costs for <paramref name="characters"/> characters (a local voice nothing).</summary>
    public static decimal Speech(CatalogModel? speech, long characters) =>
        speech is { Provider: not ModelProviders.Local } ? characters * speech.SpeechUsdPerMillionCharacters / 1_000_000m : 0;

    /// <summary>How long a narration takes to say at the estimated speaking rate (a silent scene's length; the estimate's timing).</summary>
    public static long Milliseconds(string narration, string language)
    {
        ArgumentNullException.ThrowIfNull(narration);
        var words = narration.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return (long)Math.Round(words / OutputLanguages.Of(language).WordsPerSecond * 1000);
    }
}

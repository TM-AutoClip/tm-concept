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
/// characters for an online voice. Local models and the sample images cost nothing. The exact cost of a made video is its own record.
/// </summary>
public sealed class VideoCostEstimator(ICostCalculator costs)
{
    /// <summary>Fewest output tokens of a video code answer (a short HyperFrames scene file).</summary>
    public const long CodeOutputMin = 3_000;

    /// <summary>Output tokens of the image prompt answer per scene.</summary>
    public const long PromptTokensPerScene = 120;

    /// <summary>The estimate of the given concepts; <paramref name="engine"/> null = the code prompt without the engine's own rules.</summary>
    public CostEstimate Estimate(IReadOnlyList<Concept> concepts, Profile profile, IVideoEngine? engine, CatalogModel? code, CatalogModel? image,
        CatalogModel? speech, bool subtitles)
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

            if (code is not null)
            {
                var input = RenderInputBuilder.Build(concept.Title, profile.ContentLanguage, scenes.Select(s => new RenderSceneSource(s.Visual, s.Narration,
                    string.Empty, Milliseconds(s.Narration, profile.ContentLanguage))).ToList(), width, height, 30);
                total = total.Plus(Code(input, profile.ContentLanguage, engine, code, subtitles));
                if (image is not null)
                {
                    var visuals = scenes.Select(s => s.Visual.Length > 0 ? s.Visual : s.Narration).ToList();
                    var prompt = costs.Tokens(ImagePromptBuilder.System + ImagePromptBuilder.User(profile.Name, visuals), profile.ContentLanguage);
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
                var spoken = Speech(speech, scenes.Sum(s => (long)s.Narration.Length));
                total = total.Plus(new CostEstimate(spoken, spoken, 0, 0, 0));
            }
        }

        return total;
    }

    /// <summary>
    /// The video code call for <paramref name="input"/> (its real prompt): from <see cref="CodeOutputMin"/> output tokens to the call's whole
    /// budget, plus one repair at the high end. Also the estimate of a new version with another engine (same input, new code).
    /// </summary>
    public CostEstimate Code(RenderInput input, string language, IVideoEngine? engine, CatalogModel code, bool subtitles)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(code);
        var system = engine is null ? 0 : costs.Tokens(VideoCodePrompt.System(engine, subtitles), ContentLanguages.English);
        var user = costs.Tokens(VideoCodePrompt.User(input), language);

        // The high end is the call's whole budget: 14 000 tokens used to be counted, but real answers with thinking were cut at 32 000.
        long most = VideoCodeBudget.For(code);
        var first = costs.EstimateChat(code, system + user, 0, CodeOutputMin, most);

        // One repair may follow a rejected check: its cost is the high end only.
        var repair = costs.EstimateChat(code, system + user + most, system, 0, most);
        return first with { MaxUsd = first.MaxUsd + repair.MaxUsd };
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

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
/// built from the concept's scenes; the answer is a range of <see cref="CodeOutputMin"/>–<see cref="CodeOutputMax"/> tokens, and one repair
/// may double it: the high end counts it), the image prompts and one image per scene when an image model is chosen, and the narration's
/// characters for an online voice. Local models and the sample images cost nothing. The exact cost of a made video is its own record.
/// </summary>
public sealed class VideoCostEstimator(ICostCalculator costs)
{
    /// <summary>Fewest output tokens of a video code answer (a short HyperFrames scene file).</summary>
    public const long CodeOutputMin = 3_000;

    /// <summary>Most output tokens of a video code answer counted in the estimate (thinking included).</summary>
    public const long CodeOutputMax = 14_000;

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
                var system = engine is null ? 0 : costs.Tokens(VideoCodePrompt.System(engine, subtitles), ContentLanguages.English);
                var user = costs.Tokens(VideoCodePrompt.User(input), profile.ContentLanguage);
                var first = costs.EstimateChat(code, system + user, 0, CodeOutputMin, CodeOutputMax);

                // One repair may follow a rejected check: its cost is the high end only.
                var repair = costs.EstimateChat(code, system + user + CodeOutputMax, system, 0, CodeOutputMax);
                total = total.Plus(first with { MaxUsd = first.MaxUsd + repair.MaxUsd });
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
                var characters = scenes.Sum(s => (long)s.Narration.Length);
                var spoken = characters * speech.SpeechUsdPerMillionCharacters / 1_000_000m;
                total = total.Plus(new CostEstimate(spoken, spoken, 0, 0, 0));
            }
        }

        return total;
    }

    private static long Milliseconds(string narration, string language)
    {
        var words = narration.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var rate = language == ContentLanguages.Turkish ? NarrationRate.TurkishWordsPerSecond : NarrationRate.EnglishWordsPerSecond;
        return (long)Math.Round(words / rate * 1000);
    }
}

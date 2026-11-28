using System.Text;
using TMConcept.Core.Styles;

namespace TMConcept.Core.Render;

/// <summary>
/// Makes the finishing layer of a video drawn by the app (F12, 0.1.43, <see cref="RenderFinish"/>): the style's effects
/// (<see cref="DrawStyleDefinition.Finish"/>) when the video's look has its finishing on, with a seed from the style and the video's title and
/// the boil's 12 offsets made from that seed. The same style and title always give the same layer; no model is asked. The idea of a seeded
/// stream per key follows ReelMimic's engine kit (<c>rnd(key)</c>, MIT); the generator here is our own (FNV-1a hash, SplitMix64 stream).
/// </summary>
public static class RenderFinishBuilder
{
    /// <summary>The number of boil steps a second (stop-motion "on twos" at 24 frames).</summary>
    public const int BoilSteps = 12;

    /// <summary>
    /// The layer for <paramref name="style"/> and a video titled <paramref name="title"/>; <c>null</c> when the video's look has finishing off
    /// (the profile's choice on the video page), for no style or "AI chooses" without a chosen preset, and for a style without effects.
    /// </summary>
    public static RenderFinish? For(VideoStyle? style, string title)
    {
        if (style is not { Finish: true } || style.DrawStyle == DrawStyle.Auto)
        {
            return null;
        }

        var definition = DrawStyles.Of(style.DrawStyle);
        return Build(definition.Finish, definition.Key + "|" + (title ?? string.Empty));
    }

    /// <summary>The layer of <paramref name="finish"/> seeded by <paramref name="key"/>; <c>null</c> when it has no effect.</summary>
    public static RenderFinish? Build(StyleFinish finish, string key)
    {
        ArgumentNullException.ThrowIfNull(finish);
        if (finish.IsEmpty)
        {
            return null;
        }

        var seed = Seed(key);
        var layer = new RenderFinish
        {
            Seed = seed,
            Tooth = Clamp(finish.Tooth),
            Grain = Clamp(finish.Grain),
            Vignette = Clamp(finish.Vignette),
            Bloom = Clamp(finish.Bloom),
            Shadow = Clamp(finish.Shadow),
            Boil = Clamp(finish.Boil),
        };
        if (layer.Boil > 0)
        {
            var state = (ulong)seed;
            for (var i = 0; i < BoilSteps * 2; i++)
            {
                layer.Steps.Add(Math.Round((Next(ref state) * 2) - 1, 4));
            }
        }

        return layer;
    }

    /// <summary>A 31-bit seed of <paramref name="key"/> (FNV-1a over its UTF-8 bytes; never 0), the same in every run.</summary>
    public static long Seed(string? key)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(key ?? string.Empty))
        {
            hash = unchecked((hash ^ b) * 16777619u);
        }

        var seed = hash & 0x7FFFFFFF;
        return seed == 0 ? 1 : seed;
    }

    /// <summary>The next number from 0 to 1 of a SplitMix64 stream.</summary>
    private static double Next(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / (1UL << 53));
        }
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}

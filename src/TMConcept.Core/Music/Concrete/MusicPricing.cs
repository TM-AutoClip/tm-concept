using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// The length and price of a generated piece (0.1.46, C5; prices from the catalog, updated by <c>pricing/prices.json</c>): a model with a fixed
/// piece length makes that (Lyria 3 Clip: 30 s, $0.04); a model billed per song makes one song (Lyria 3.5: $0.08); a model billed per minute
/// makes the video's length up to <see cref="MusicGenerationRules.MaxSeconds"/> (ElevenLabs Music: $0.15 a minute). A shorter piece is looped
/// (F8); no second piece is ever made. The local model costs nothing. Pure.
/// </summary>
public static class MusicPricing
{
    /// <summary>The seconds asked of <paramref name="model"/> for a video of <paramref name="videoSeconds"/>.</summary>
    public static int Seconds(CatalogModel model, double videoSeconds)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.MusicSeconds > 0)
        {
            return model.MusicSeconds;
        }

        if (model.MusicUsdPerPiece > 0)
        {
            return MusicGenerationRules.SongSeconds;
        }

        var wanted = double.IsFinite(videoSeconds) ? (int)Math.Ceiling(videoSeconds) : MusicGenerationRules.MinSeconds;
        return Math.Clamp(wanted, MusicGenerationRules.MinSeconds, MusicGenerationRules.MaxSeconds);
    }

    /// <summary>What <paramref name="seconds"/> of <paramref name="model"/>'s music cost (rounded to a millionth of a dollar).</summary>
    public static decimal Usd(CatalogModel model, int seconds)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Provider == ModelProviders.Local)
        {
            return 0;
        }

        return model.MusicUsdPerPiece > 0
            ? model.MusicUsdPerPiece
            : decimal.Round(model.MusicUsdPerMinute * seconds / 60m, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Whether the price is exact (per piece) rather than worked out from the length (per minute; the provider's rounding unknown).</summary>
    public static bool Exact(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local || model.MusicUsdPerPiece > 0;
    }
}

namespace TMConcept.Core.Videos;

/// <summary>Output budget of a video code call (and of its single repair).</summary>
public static class VideoCodeBudget
{
    /// <summary>
    /// Most output tokens of one video code answer (thinking included), lowered to the model's own limit when that is smaller. 32 000 was too
    /// little: two Opus 5.5 answers (effort high, HyperFrames) were cut at it after 325 s and 314 s, about 0.65 $ each (7 Nov 2026), because
    /// the adaptive thinking takes from the same budget. A 64 000-token answer takes about 11 minutes, so the call streams.
    /// </summary>
    public const int MaxOutputTokens = 64_000;

    /// <summary>The budget for <paramref name="model"/>: <see cref="MaxOutputTokens"/>, or the model's limit when it is smaller.</summary>
    public static int For(Models.CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.MaxOutputTokens > 0 ? Math.Min(MaxOutputTokens, model.MaxOutputTokens) : MaxOutputTokens;
    }
}

using TMConcept.Core.Videos;

namespace TMConcept.Desktop.Localization;

/// <summary>Why a video run failed, in words: the video page's queue rows and a new version of a produced video use the same texts.</summary>
public static class VideoFailures
{
    // The provider's code of an answer cut at its output limit.
    private const string Truncated = "truncated";

    /// <summary>Whether a failure code is a video code answer cut at its output limit (its text already says what was spent).</summary>
    public static bool IsTruncated(string code) => code == VideoException.ProviderPrefix + Truncated;

    /// <summary>
    /// The reason of a failure. A cut-off answer here is the video code (the image prompts fall back to the scene texts): the shared provider
    /// text speaks of concepts kept before the cut, which means nothing for a video, so it has its own text with what was paid.
    /// </summary>
    public static string Reason(Localizer text, string code, string detail, decimal spentUsd)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(code);
        if (!code.StartsWith(VideoException.ProviderPrefix, StringComparison.Ordinal))
        {
            return text["video.error." + code] + " " + detail;
        }

        var provider = code[VideoException.ProviderPrefix.Length..];
        return provider == Truncated ? text.Format("video.error.truncated", text.Usd(spentUsd)) : ProviderErrors.Text(text, provider);
    }
}

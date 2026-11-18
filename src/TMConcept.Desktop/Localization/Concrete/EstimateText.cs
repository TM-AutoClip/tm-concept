using System.Globalization;
using TMConcept.Core.Documents;

namespace TMConcept.Desktop.Localization;

/// <summary>
/// The user's line of a <see cref="WorkEstimate"/> (0.1.24): the price range ("0.42 $–0.61 $") or "free, on this computer", and a rough time
/// ("≈ 12–25 min", "≈ 2–4 h", "under a minute"). The caller says it is an estimate.
/// </summary>
public static class EstimateText
{
    /// <summary>"0.42 $–0.61 $ · ≈ 12–25 min".</summary>
    public static string Line(Localizer text, WorkEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(estimate);
        var price = estimate.Local && estimate.Cost.MaxUsd == 0
            ? text["estimate.free"]
            : text.Usd(estimate.Cost.MinUsd) + "–" + text.Usd(estimate.Cost.MaxUsd);
        return price + " · " + Time(text, estimate.TimeMin, estimate.TimeMax);
    }

    /// <summary>"≈ 12–25 min" (minutes up to two hours, then hours), or "under a minute".</summary>
    public static string Time(Localizer text, TimeSpan low, TimeSpan high)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (high < TimeSpan.FromMinutes(1))
        {
            return text["estimate.seconds"];
        }

        if (high < TimeSpan.FromHours(2))
        {
            return text.Format("estimate.minutes", Math.Max(1, (int)Math.Round(low.TotalMinutes)), (int)Math.Ceiling(high.TotalMinutes));
        }

        return text.Format("estimate.hours", Math.Round(low.TotalHours, low.TotalHours < 10 ? 1 : 0).ToString(text.Culture),
            Math.Ceiling(high.TotalHours).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>"410k", "1.2M", "950" tokens.</summary>
    public static string Tokens(Localizer text, long tokens)
    {
        ArgumentNullException.ThrowIfNull(text);
        return tokens >= 1_000_000
            ? (tokens / 1_000_000.0).ToString("0.#", text.Culture) + "M"
            : tokens >= 10_000 ? (tokens / 1_000).ToString(text.Culture) + "k" : tokens.ToString("N0", text.Culture);
    }
}

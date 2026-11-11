namespace TMConcept.Core.Concepts;

/// <summary>
/// Sizes of what the concept and topic prompts tell the model about a profile's past (10 Nov 2026, the developer: "as short as possible to
/// cut cost and not eat the context"). 0.1.9 listed up to 1 500 titles one by one (about 25 000 tokens); the covered block is now capped at
/// <see cref="CoveredMaxCharacters"/> (about 1 000 tokens) whatever the history, the dislikes and wishes add at most about 1 500 more. The
/// local duplicate check (words, optionally meaning) stays the hard guard against repeats: the prompt only steers.
/// </summary>
public static class PromptMemoryLimits
{
    /// <summary>Newest titles listed one by one in the covered block.</summary>
    public const int CoveredNewest = 40;

    /// <summary>Most lines the older titles are condensed into (series and themes with counts).</summary>
    public const int CoveredThemeLines = 15;

    /// <summary>Hard cap of the whole covered block (characters).</summary>
    public const int CoveredMaxCharacters = 3_000;

    /// <summary>
    /// Part of the cap the newest titles leave to the condensed older lines (when there are that many): 40 newest titles of 80 characters
    /// would fill the whole cap alone and the older history would vanish.
    /// </summary>
    public const int CoveredOlderReserve = 1_000;

    /// <summary>Longest example title inside a theme line.</summary>
    public const int ThemeExampleMax = 40;

    /// <summary>A word shared by more than this percent of the older titles names no theme (on a single-subject channel it is the subject itself).</summary>
    public const int ThemeShareMax = 40;

    /// <summary>The share rule of <see cref="ThemeShareMax"/> applies from this many older titles on (a few titles share words by chance).</summary>
    public const int ThemeShareMinTitles = 10;

    /// <summary>Newest dislikes of each kind (topic, telling) sent along.</summary>
    public const int DislikesEach = 10;

    /// <summary>Longest opening sentence (or summary) of a "telling" dislike.</summary>
    public const int DislikeSummaryMax = 160;

    /// <summary>AI-edit instructions kept (and sent) per profile.</summary>
    public const int WishesKept = 8;

    /// <summary>Longest wish sent (characters).</summary>
    public const int WishMax = 200;

    /// <summary>
    /// Earlier topic suggestions listed in the topic prompt (the newest; 0.1.9 sent 150, 0.1.11 sent 60). A press can now bring 100 topics,
    /// so 60 did not even cover the last press; 200 short lines are about 6 000 input tokens on the cheap topics model. The local repeat
    /// filter still checks every one.
    /// </summary>
    public const int SuggestionsSent = 200;
}

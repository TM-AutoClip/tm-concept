namespace TMConcept.Core.Research;

/// <summary>
/// What one research run is asked for: the subject, the profile's language (concepts are written in it, sources may be in any), extra
/// languages the user wants read ("fr, de"; empty = only what the subject suggests), the user's own addresses and the limits.
/// </summary>
public sealed record ResearchRequest(string Subject, string Language)
{
    /// <summary>Languages the user added.</summary>
    public IReadOnlyList<string> ExtraLanguages { get; init; } = [];

    /// <summary>Addresses the user gave (fetched as they are, no search).</summary>
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>Pages fetched at most (1–<see cref="ResearchLimits.PagesMax"/>).</summary>
    public int MaxPages { get; init; } = ResearchLimits.PagesDefault;

    /// <summary>Bytes all fetched pages may hold together.</summary>
    public long MaxBytes { get; init; } = ResearchLimits.BytesMax;

    /// <summary>Longest the search, and again the fetching, may take.</summary>
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(ResearchLimits.SecondsMax);
}

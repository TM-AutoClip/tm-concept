namespace TMConcept.Core.Music;

/// <summary>A made piece (0.1.46, C5): its file, what it cost and whether that cost is the provider's exact price.</summary>
/// <param name="File">The piece (full path; mp3, wav or flac).</param>
/// <param name="Usd">What it cost (0 for the local model).</param>
/// <param name="Exact">Whether <paramref name="Usd"/> is exact (a price per piece) or worked out from the length (a price per minute).</param>
public sealed record GeneratedMusic(string File, decimal Usd, bool Exact);

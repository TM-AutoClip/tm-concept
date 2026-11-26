namespace TMConcept.Core.Music;

/// <summary>
/// Makes one piece with a music model (0.1.55; the music page and the video's music step had a copy each): a local model made for the graphics
/// card holds the card's lock while it runs (one local model on the card at a time, rule 18), an online model runs as it is. Every failure
/// comes out as <see cref="MusicGenerationException"/> (a memory shortfall as <see cref="MusicFailure.Memory"/>; an online provider's own
/// error keeps its message), never as a stray exception; a cancellation stays a cancellation.
/// </summary>
public interface IMusicPieceRunner
{
    /// <summary>Makes <paramref name="request"/> with <paramref name="generator"/>.</summary>
    Task<GeneratedMusic> RunAsync(IMusicGenerator generator, MusicGenerationRequest request, CancellationToken cancellationToken);
}

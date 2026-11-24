using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// Makes a piece of music for a concept (0.1.46, C5): the local ACE-Step 1.5 through rakkase, or an online model (Google Lyria, ElevenLabs
/// Music). One per provider, registered in DI; <see cref="IMusicGenerators"/> picks the one of a template's model.
/// </summary>
public interface IMusicGenerator
{
    /// <summary>Whether it makes the music of <paramref name="model"/> (a catalog entry with <see cref="ModelCapability.Music"/>).</summary>
    bool Handles(CatalogModel model);

    /// <summary>Whether <paramref name="model"/> can be used now (a local model: its components installed; files only, no process).</summary>
    bool Ready(CatalogModel model);

    /// <summary>
    /// Makes the piece into <see cref="MusicGenerationRequest.OutputFolder"/> (network or a local process). A failure throws
    /// <see cref="MusicGenerationException"/> (an online provider's error may come as its own exception).
    /// </summary>
    Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken);
}

namespace TMConcept.Core.Styles;

/// <summary>The user's own colour palettes (<see cref="SavedPalette"/>), at most <see cref="VideoStyleLimits.PalettesMax"/>.</summary>
public interface IVideoPaletteStore
{
    /// <summary>Every saved palette, by name.</summary>
    Task<IReadOnlyList<SavedPalette>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds or replaces (same id). <see cref="ArgumentException"/> when the name is empty or too long or the colours do not pass
    /// <see cref="PaletteDesigner.Check"/>; <see cref="VideoStyleLimitException"/> when a new one would make more than
    /// <see cref="VideoStyleLimits.PalettesMax"/>. Nothing is written then.
    /// </summary>
    Task SaveAsync(SavedPalette palette, CancellationToken cancellationToken);

    /// <summary>Deletes a saved palette (templates that use it keep their copy); a missing id is not an error.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

namespace TMConcept.Core.Styles;

/// <summary>
/// A colour palette the user made and named (0.1.24, Settings → Templates): kept in the encrypted database (<see cref="IVideoPaletteStore"/>)
/// so any template can pick it again. A template that uses it keeps its own copy of the colours (<see cref="VideoStyle.CustomPalette"/>),
/// so changing or deleting the saved palette never changes a template or a video.
/// </summary>
public sealed class SavedPalette
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Name (at most <see cref="VideoStyleLimits.NameMax"/> characters).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The colours (its id is <see cref="VideoPalettes.CustomId"/>).</summary>
    public VideoPalette Colors { get; set; } = VideoPalettes.All[0] with { Id = VideoPalettes.CustomId };

    /// <summary>Created (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

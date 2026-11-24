using TMConcept.Core.Speech;

namespace TMConcept.Core.Scenes;

/// <summary>
/// Reads the drawer's transition catalog (F1, 0.1.42, <see cref="SceneTransitions.Catalog"/>): the energy of a tempo, the names allowed at an
/// energy, what a name means, how long it lasts and the default of an energy or a drawing style's transition family.
/// </summary>
public static class TransitionCatalog
{
    /// <summary>The energy of a narration tempo: relaxed = calm, natural = medium, tight = high (an unknown value reads as medium).</summary>
    public static TransitionEnergy EnergyOf(NarrationTempo tempo) => tempo switch
    {
        NarrationTempo.Relaxed => TransitionEnergy.Calm,
        NarrationTempo.Tight => TransitionEnergy.High,
        _ => TransitionEnergy.Medium,
    };

    /// <summary>Every full name allowed at <paramref name="energy"/>, in catalog order (a directional one once per direction).</summary>
    public static IReadOnlyList<string> Names(TransitionEnergy energy) =>
        SceneTransitions.Catalog.Where(d => d.Energies.Contains(energy)).SelectMany(Names).ToList();

    /// <summary>Every full name of <paramref name="definition"/> (one per direction for a directional one).</summary>
    public static IEnumerable<string> Names(TransitionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Directional ? SceneTransitions.Directions.Select(d => Name(definition.Family, d)) : [definition.Family];
    }

    /// <summary>The full name of a family in a direction (<c>push-left</c>); a family without direction keeps its name.</summary>
    public static string Name(string family, string direction) =>
        Family(family) is { Directional: true } ? family + "-" + direction : family;

    /// <summary>The definition of a family name (null when there is none).</summary>
    public static TransitionDefinition? Family(string? family) =>
        SceneTransitions.Catalog.FirstOrDefault(d => string.Equals(d.Family, family, StringComparison.Ordinal));

    /// <summary>
    /// What a full name means: its definition and its direction (empty for one without direction); null for a name that is not in the catalog
    /// (a directional family without a known direction, or a direction on one that has none).
    /// </summary>
    public static (TransitionDefinition Definition, string Direction)? Find(string? name)
    {
        var text = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (Family(text) is { Directional: false } plain)
        {
            return (plain, string.Empty);
        }

        var dash = text.LastIndexOf('-');
        if (dash <= 0 || Family(text[..dash]) is not { Directional: true } directional || !SceneTransitions.Directions.Contains(text[(dash + 1)..]))
        {
            return null;
        }

        return (directional, text[(dash + 1)..]);
    }

    /// <summary>Whether <paramref name="name"/> is in the catalog and may be used at <paramref name="energy"/>.</summary>
    public static bool Allowed(string? name, TransitionEnergy energy) => Find(name) is { } found && found.Definition.Energies.Contains(energy);

    /// <summary>Seconds of a family at an energy (a cut takes none, a zoom-through always the same).</summary>
    public static double Seconds(string family, TransitionEnergy energy) => family switch
    {
        SceneTransitions.Cut => 0,
        SceneTransitions.ZoomThrough => SceneTransitions.ZoomThroughSeconds,
        _ => energy switch
        {
            TransitionEnergy.Calm => SceneTransitions.CalmSeconds,
            TransitionEnergy.High => SceneTransitions.HighSeconds,
            _ => SceneTransitions.MediumSeconds,
        },
    };

    /// <summary>
    /// The family a scene gets when the plan chose none (or one that does not fit): the drawing style's family when it fits the energy
    /// (<paramref name="styleFamily"/>, empty = none), otherwise the energy's: calm = crossfade, medium = slide, high = push.
    /// </summary>
    public static string DefaultFamily(TransitionEnergy energy, string styleFamily)
    {
        if (Family(styleFamily) is { } style && style.Energies.Contains(energy))
        {
            return style.Family;
        }

        return energy switch
        {
            TransitionEnergy.Calm => SceneTransitions.Crossfade,
            TransitionEnergy.High => SceneTransitions.Push,
            _ => SceneTransitions.Slide,
        };
    }
}

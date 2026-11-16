namespace TMConcept.Core.Hardware;

/// <summary>
/// Graphics card memory classes (GB) used to say which local models a card can run: a model names the smallest class it runs in, a card
/// belongs to one class (<see cref="VramTiers.ForCard"/>, with a tolerance for what the driver reports) and sees its own class and every
/// lower one (<see cref="VramTiers.Sees"/>). The value is the class in GB.
/// </summary>
public enum VramTier
{
    /// <summary>No class: no graphics card (or its memory unknown) for a card; no graphics card needed for a model.</summary>
    None = 0,

    /// <summary>4 GB.</summary>
    Gb4 = 4,

    /// <summary>6 GB.</summary>
    Gb6 = 6,

    /// <summary>8 GB.</summary>
    Gb8 = 8,

    /// <summary>12 GB.</summary>
    Gb12 = 12,

    /// <summary>16 GB.</summary>
    Gb16 = 16,

    /// <summary>24 GB.</summary>
    Gb24 = 24,

    /// <summary>32 GB (and more).</summary>
    Gb32 = 32,
}
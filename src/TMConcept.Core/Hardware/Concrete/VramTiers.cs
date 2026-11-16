namespace TMConcept.Core.Hardware;

/// <summary>
/// The memory classes of graphics cards and models (the developer, 16 Nov 2026: 4/6/8/12/16/24/32 GB; a card sees its own class and every
/// lower one, a higher one is shown in red as not usable). A card's class comes from the total memory its driver reports, with
/// <see cref="Tolerance"/> for the driver's reporting gap only: a "16 GB" card reports 15.8-16 GiB (Windows keeping about an eighth for itself does not lower the class:
/// that is what <see cref="IVramProbe"/>'s free memory is for).
/// </summary>
public static class VramTiers
{
    /// <summary>
    /// A card counts in a class when it reports at most this share less than the class (8 %: 14.8 GiB is still a 16 GB card, but an 11 GiB
    /// card such as a GTX 1080 Ti or RTX 2080 Ti is in the 8 GB class: it physically has less than 12 GB).
    /// </summary>
    public const double Tolerance = 0.08;

    private const double Gibibyte = 1024.0 * 1024 * 1024;

    /// <summary>Every class from the smallest up (<see cref="VramTier.None"/> left out).</summary>
    public static IReadOnlyList<VramTier> All { get; } = [VramTier.Gb4, VramTier.Gb6, VramTier.Gb8, VramTier.Gb12, VramTier.Gb16, VramTier.Gb24, VramTier.Gb32];

    /// <summary>
    /// The class of a card that reports <paramref name="totalBytes"/> of own memory: the largest class it reaches within <see cref="Tolerance"/>
    /// (15.8 GiB → 16 GB, 11.5 GiB → 12 GB, 11 GiB → 8 GB, 3.6 GiB → none). None when the memory is unknown.
    /// </summary>
    public static VramTier ForCard(long? totalBytes)
    {
        if (totalBytes is not > 0)
        {
            return VramTier.None;
        }

        var gibibytes = totalBytes.Value / Gibibyte;
        return All.LastOrDefault(t => gibibytes >= (int)t * (1 - Tolerance));
    }

    /// <summary>
    /// The class of a model that needs <paramref name="gigabytes"/> of graphics card memory: the smallest class that holds it (6 → 6 GB, 7 → 8 GB,
    /// more than 32 → 32 GB); 0 or less = none (no graphics card needed).
    /// </summary>
    public static VramTier ForModel(double gigabytes)
    {
        if (gigabytes <= 0)
        {
            return VramTier.None;
        }

        return All.FirstOrDefault(t => (int)t >= gigabytes - 0.001, VramTier.Gb32);
    }

    /// <summary>Whether a card of class <paramref name="card"/> can run a model of class <paramref name="model"/>: its own class and every lower one.</summary>
    public static bool Sees(VramTier card, VramTier model) => model == VramTier.None || (card != VramTier.None && (int)model <= (int)card);

    /// <summary>The classes a card of class <paramref name="card"/> sees (lowest first; empty for no card).</summary>
    public static IReadOnlyList<VramTier> Visible(VramTier card) => All.Where(t => Sees(card, t)).ToList();

    /// <summary>The class in GB (0 for none).</summary>
    public static int Gigabytes(VramTier tier) => (int)tier;
}
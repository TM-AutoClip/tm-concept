using TMConcept.Core.References;

namespace TMConcept.Core.Channels;

/// <summary>
/// Puts the measured samples of a channel together into one tempo profile ("Kanaldan esinlen", 0.1.53): every number is the median of the
/// samples (one odd sample does not pull it), the camera's way and its ease are the most common ones (ties: the first sample's), sound counts
/// when most samples have it, the beat is the median of the samples that have one; the colours and the strongest moments are those of the
/// sample nearest the median cut rate (a real video's, not a mix of colours no sample had). Pure.
/// </summary>
public static class TempoProfileMerger
{
    /// <summary>The merged profile of <paramref name="profiles"/> (at least one), named <paramref name="source"/>.</summary>
    public static ReferenceTempoProfile Merge(IReadOnlyList<ReferenceTempoProfile> profiles, string source, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            throw new ArgumentException("At least one measured profile is needed.", nameof(profiles));
        }

        var cuts = Median(profiles.Select(p => p.CutsPer10Seconds));
        var nearest = profiles.Select((p, i) => (Profile: p, Order: i)).OrderBy(x => Math.Abs(x.Profile.CutsPer10Seconds - cuts)).ThenBy(x => x.Order).First().Profile;
        var beats = profiles.Where(p => p.Bpm > 0).Select(p => p.Bpm).ToList();
        return new ReferenceTempoProfile
        {
            Source = source,
            FromUrl = true,
            MeasuredAtUtc = now,
            Seconds = Math.Round(Median(profiles.Select(p => p.Seconds)), 1),
            Cuts = (int)Math.Round(Median(profiles.Select(p => (double)p.Cuts))),
            CutsPer10Seconds = Math.Round(cuts, 2),
            AverageShotSeconds = Math.Round(Median(profiles.Select(p => p.AverageShotSeconds)), 2),
            CameraShare = Math.Round(Median(profiles.Select(p => p.CameraShare)), 2),
            CameraDirection = Mode(profiles.Select(p => p.CameraDirection)),
            Ease = Mode(profiles.Select(p => p.Ease)),
            LabL = Math.Round(Median(profiles.Select(p => p.LabL)), 1),
            LabA = Math.Round(Median(profiles.Select(p => p.LabA)), 1),
            LabB = Math.Round(Median(profiles.Select(p => p.LabB)), 1),
            Brightness = Math.Round(Median(profiles.Select(p => p.Brightness)), 3),
            Saturation = Math.Round(Median(profiles.Select(p => p.Saturation)), 3),
            Palette = [.. nearest.Palette],
            HasSound = profiles.Count(p => p.HasSound) * 2 > profiles.Count,
            Bpm = beats.Count == 0 ? 0 : Math.Round(Median(beats), 1),
            Peaks = [.. nearest.Peaks],
        };
    }

    /// <summary>The median of <paramref name="values"/> (the mean of the middle two for an even count; 0 for none).</summary>
    public static double Median(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(double.IsFinite).Order().ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static T Mode<T>(IEnumerable<T> values)
        where T : struct, Enum =>
        values.Select((v, i) => (Value: v, Order: i)).GroupBy(x => x.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Min(x => x.Order)).First().Key;
}

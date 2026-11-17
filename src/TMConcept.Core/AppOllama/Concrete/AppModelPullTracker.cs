using System.Globalization;
using TMConcept.Core.Tools;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// Turns the lines of an Ollama pull into one share for the install window: the bytes received of every layer so far over the whole model
/// (the catalogue's size, or the layers' own totals when they say more), with "1.2 / 6.1 GB" as the text. A layer Ollama already had (a
/// resumed pull) counts as received at once. Not thread-safe: one pull reports in order.
/// </summary>
public sealed class AppModelPullTracker(long expectedBytes)
{
    private readonly Dictionary<string, (long Total, long Completed)> _layers = new(StringComparer.Ordinal);

    /// <summary>The share received so far (0–1).</summary>
    public double Share { get; private set; }

    /// <summary>Takes one progress line and gives the overall progress.</summary>
    public ToolProgress Add(AppModelPullProgress line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Digest.Length > 0 && line.Total > 0)
        {
            _layers[line.Digest] = (line.Total, Math.Clamp(line.Completed, 0, line.Total));
        }

        var total = Math.Max(expectedBytes, _layers.Values.Sum(l => l.Total));
        var received = _layers.Values.Sum(l => l.Completed);
        Share = line.Success ? 1 : total > 0 ? Math.Clamp((double)received / total, 0, 0.99) : 0;
        var text = total > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{received / 1e9:0.0} / {total / 1e9:0.0} GB")
            : line.Status;
        return new ToolProgress(Share, line.Success ? line.Status : text);
    }
}

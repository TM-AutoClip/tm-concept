using System.Globalization;
using TMConcept.Core.Activity;
using TMConcept.Core.Models;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.Memory;

/// <summary>
/// <see cref="IMemoryNotices"/> as lines of the activity panel in the user's language (every line goes to the log file too): the steps of a
/// memory check as information ("Ekran kartı belleği ölçülüyor (1/2)…"), a placement off the card and the processor build as yellow
/// warnings, a failure in night mode as a red error. A model's placement is said once until the next memory check (an image loop places
/// the model before every picture).
/// </summary>
public sealed class MemoryNoticeLog(IActivityLog activity, Localizer text) : IMemoryNotices
{
    private const double Gigabyte = 1024.0 * 1024 * 1024;

    private readonly Lock _gate = new();
    private string? _lastPlaced;

    /// <inheritdoc />
    public void Report(MemoryNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var name = notice.ModelName;
        switch (notice.Kind)
        {
            case MemoryNoticeKind.Settling:
                Forget();
                activity.Add(ActivityLevel.Info, text.Format("memory.notice.settling", name, Seconds(notice.Wait)));
                break;
            case MemoryNoticeKind.Measuring:
                Forget();
                activity.Add(ActivityLevel.Info, text.Format("memory.notice.measuring", name, notice.Step, notice.Of));
                break;
            case MemoryNoticeKind.Asking:
                activity.Add(ActivityLevel.Warning, text.Format("memory.notice.asking", name));
                break;
            case MemoryNoticeKind.Retrying:
                activity.Add(ActivityLevel.Info, text.Format("memory.notice.retrying", name, Seconds(notice.Wait)));
                break;
            case MemoryNoticeKind.Enough:
                activity.Add(ActivityLevel.Info, text.Format("memory.notice.enough", name));
                break;
            case MemoryNoticeKind.Placed when First(notice):
                activity.Add(ActivityLevel.Warning, text.Format(notice.Placement == MemoryPlacement.Disk ? "memory.notice.disk" : "memory.notice.main", name));
                break;
            case MemoryNoticeKind.ProcessorFallback when First(notice):
                activity.Add(ActivityLevel.Warning, text.Format("memory.notice.processor", name));
                break;
            case MemoryNoticeKind.Failed:
                activity.Add(ActivityLevel.Error, text.Format("memory.notice.failed", name, Figures(notice.Shortfall)));
                break;
            case MemoryNoticeKind.Canceled:
                activity.Add(ActivityLevel.Warning, text.Format("memory.notice.canceled", name));
                break;
        }
    }

    /// <summary>The figures of a shortfall in one short line ("4,3 / 5,9 GB boş, 5,5 GB gerekiyor").</summary>
    public string Figures(MemoryShortfall? shortfall) => shortfall is null
        ? string.Empty
        : text.Format("memory.notice.figures", Gb(shortfall.CardFreeBytes), Gb(shortfall.CardTotalBytes), Gb(shortfall.NeededVideoBytes));

    private static string Seconds(TimeSpan wait) => wait.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture);

    private static double Gb(long bytes) => Math.Round(Math.Max(0, bytes) / Gigabyte, 1);

    private void Forget()
    {
        lock (_gate)
        {
            _lastPlaced = null;
        }
    }

    private bool First(MemoryNotice notice)
    {
        var key = notice.Kind + "|" + notice.ModelKey + "|" + notice.Placement;
        lock (_gate)
        {
            if (key == _lastPlaced)
            {
                return false;
            }

            _lastPlaced = key;
            return true;
        }
    }
}

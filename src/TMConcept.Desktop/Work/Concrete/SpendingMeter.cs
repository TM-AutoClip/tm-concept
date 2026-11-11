using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.Work;

/// <summary>
/// The money behind the spending pill at the top left of the main window: this month's total (read from the runs and videos of every profile)
/// and what the running paid works have spent so far. Every paid work reports its own spending under its owner (<see cref="Live"/>) and
/// ends it (<see cref="EndAsync"/>): its run is saved by then, so the month is read again and the live part leaves the sum, which keeps a
/// cost from being counted twice. Used on the interface thread like <see cref="WorkStatus"/> (a call from another thread is posted there).
/// </summary>
public sealed class SpendingMeter(IRunStore runs, IProfileStore profiles, IVideoStore videos, TimeProvider clock, Localizer text,
    ILogger<SpendingMeter> logger)
{
    private readonly List<(object Owner, decimal Usd)> _live = [];
    private int _refresh;

    /// <summary>The month, the live part or the breakdown changed.</summary>
    public event EventHandler? Changed;

    /// <summary>This month's total (every profile) as of the last read.</summary>
    public decimal MonthUsd { get; private set; }

    /// <summary>What the running works have spent so far (0 when none runs or none paid yet).</summary>
    public decimal LiveUsd => _live.Sum(l => l.Usd);

    /// <summary>The month's parts in the current language ("anthropic 0.80 $ · videos 0.40 $"); empty when nothing was paid.</summary>
    public string Breakdown { get; private set; } = string.Empty;

    /// <summary>Sets what <paramref name="owner"/>'s running work has spent so far (replaces its earlier value).</summary>
    public void Live(object owner, decimal usd)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Live(owner, usd));
            return;
        }

        var at = _live.FindIndex(l => ReferenceEquals(l.Owner, owner));
        if (at >= 0 && _live[at].Usd == usd)
        {
            return;
        }

        if (at >= 0)
        {
            _live[at] = (owner, usd);
        }
        else
        {
            _live.Add((owner, usd));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary><paramref name="owner"/>'s work ended (its run is saved): its live part leaves and the month is read again.</summary>
    public Task EndAsync(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.InvokeAsync(() => EndAsync(owner));
        }

        if (_live.RemoveAll(l => ReferenceEquals(l.Owner, owner)) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return RefreshAsync();
    }

    /// <summary>
    /// Reads this month's total and parts again. A read that fails (the database is not open yet) keeps the last values and is logged; when
    /// reads overlap, only the newest one is shown.
    /// </summary>
    public async Task RefreshAsync()
    {
        var mine = Interlocked.Increment(ref _refresh);
        Spending spending;
        try
        {
            spending = await PriceInfo.SpendingAsync(runs, profiles, videos, clock, text, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException
                                       or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(ex, "This month's spending could not be read");
            return;
        }

        if (mine != Volatile.Read(ref _refresh))
        {
            return;
        }

        MonthUsd = spending.TotalUsd;
        Breakdown = spending.Breakdown;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

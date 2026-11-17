using Avalonia.Threading;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.Views;

namespace TMConcept.Desktop.Dialogs;

/// <summary>
/// <see cref="IMemoryShortfallHandler"/> as a normal (not danger) question window over the active window, on the interface thread: the card's
/// total and free memory with its source, what the model needs, the free main memory, what to do ("close games, browsers… and press Retry;
/// otherwise Cancel") and the hint to choose another model. Retry / Cancel. A notification is shown too, so a window in the background is
/// noticed. The window closes when the queue is canceled (the token) and the call throws <see cref="OperationCanceledException"/>; no
/// window to show it over counts as Cancel.
/// </summary>
public sealed class MemoryShortfallDialog(WindowHost host, Localizer text, INotifier notifier) : IMemoryShortfallHandler
{
    private const double Gigabyte = 1024.0 * 1024 * 1024;

    /// <inheritdoc />
    public Task<MemoryShortfallChoice> AskAsync(MemoryShortfall shortfall, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shortfall);
        cancellationToken.ThrowIfCancellationRequested();
        notifier.Notify(new AppNotification(NotificationKind.Warning, Title(shortfall), text["memory.shortfall.close"]));
        return Dispatcher.UIThread.InvokeAsync(() => ShowAsync(shortfall, cancellationToken));
    }

    /// <summary>The question's text with the figures (in GB, one decimal).</summary>
    public string Message(MemoryShortfall shortfall)
    {
        ArgumentNullException.ThrowIfNull(shortfall);
        var main = shortfall.FreeMainBytes is { } free ? text.Format("memory.gb", Gb(free)) : text["memory.unknown"];
        var mainNeed = shortfall.NeededMainBytes > 0 ? text.Format("memory.shortfall.main_need", Gb(shortfall.NeededMainBytes)) : string.Empty;
        return text.Format("memory.shortfall.message", shortfall.ModelName, Gb(shortfall.CardTotalBytes), Gb(shortfall.CardFreeBytes), shortfall.Source,
            Gb(shortfall.NeededVideoBytes), mainNeed, main);
    }

    private string Title(MemoryShortfall shortfall) => text.Format("memory.shortfall.title", shortfall.ModelName);

    private async Task<MemoryShortfallChoice> ShowAsync(MemoryShortfall shortfall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (host.Active is not { } owner)
        {
            return MemoryShortfallChoice.Cancel;
        }

        var model = new ConfirmViewModel(Title(shortfall), Message(shortfall), text["memory.shortfall.retry"], text["common.cancel"], false, null);
        var window = new ConfirmWindow { DataContext = model };
        using (cancellationToken.Register(() => Dispatcher.UIThread.Post(window.Close)))
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return model.Confirmed ? MemoryShortfallChoice.Retry : MemoryShortfallChoice.Cancel;
    }

    private static double Gb(long bytes) => Math.Round(Math.Max(0, bytes) / Gigabyte, 1);
}

using TMConcept.Core.Components;

namespace TMConcept.Core.DiskSpace;

/// <summary>
/// The installed components as storage rows: every component with its own folders that is ready or takes space (a half-finished download or
/// weights whose program was removed are listed as incomplete, so they can be removed). The system's own copy of a program is not the app's
/// space and is not listed.
/// </summary>
public sealed class ComponentDiskSource(ComponentPlanner planner) : IDiskItemSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IDiskItem>> ListAsync(UsageMap usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usage);
        await planner.RefreshAllAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => planner.All.Select(c =>
        {
            if (c.Folders().Count == 0)
            {
                return null;
            }

            var ready = c.Ready;
            var bytes = planner.Size(c);
            if (!ready && bytes == 0)
            {
                return null;
            }

            var state = !ready ? DiskItemState.Incomplete : usage.IsUsed(c.Code) ? DiskItemState.Used : DiskItemState.Unused;
            return (IDiskItem)new ComponentDiskItem(c, planner, bytes, state, usage.For(c.Code));
        }).OfType<IDiskItem>().ToList(), cancellationToken).ConfigureAwait(false);
    }
}

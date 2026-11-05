using System.Collections.ObjectModel;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// Brings a bound list to a wanted content with the fewest changes (remove what is gone, insert or move the rest into place) instead of
/// clearing it. A list that is cleared drops the selection of its list box, which closed the open card and stopped the player every time
/// a background job reloaded a page.
/// </summary>
public static class CollectionSync
{
    /// <summary>Makes <paramref name="target"/> equal to <paramref name="wanted"/> (same objects, same order).</summary>
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> wanted)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(wanted);
        var keep = new HashSet<T>(wanted, ReferenceEqualityComparer.Instance);
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var item = wanted[i];
            var at = IndexOf(target, item, i);
            if (at == i)
            {
                continue;
            }

            if (at < 0)
            {
                target.Insert(i, item);
            }
            else
            {
                target.Move(at, i);
            }
        }

        while (target.Count > wanted.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> list, T item, int from)
        where T : class
    {
        for (var i = from; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}

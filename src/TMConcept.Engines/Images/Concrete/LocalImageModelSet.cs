namespace TMConcept.Engines.Images;

/// <summary>
/// Every local image model the app offers: the built-in ones (<see cref="LocalImageModels.All"/>, always, unchanged) and those the
/// repository's model catalog added (0.1.26, checked by the desktop app's remote catalog reader and added once at start, before anything
/// reads the list). An id that exists already is never replaced, so the built-in list is the floor.
/// </summary>
public static class LocalImageModelSet
{
    private static readonly Lock Gate = new();
    private static volatile IReadOnlyList<LocalImageModel> _all = LocalImageModels.All;

    /// <summary>The built-in models, then the added ones.</summary>
    public static IReadOnlyList<LocalImageModel> All => _all;

    /// <summary>Adds the models whose id is new; returns how many were added.</summary>
    public static int Add(IEnumerable<LocalImageModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        lock (Gate)
        {
            var added = models.Where(m => _all.All(e => e.Id != m.Id)).DistinctBy(m => m.Id).ToList();
            if (added.Count > 0)
            {
                _all = [.. _all, .. added];
            }

            return added.Count;
        }
    }

    /// <summary>The model of <paramref name="id"/>; null when the app does not offer it.</summary>
    public static LocalImageModel? Find(string id) => _all.FirstOrDefault(m => m.Id == id);
}

namespace TMConcept.Core.AppOllama;

/// <summary>
/// Every model the app offers for its own Ollama copy: the built-in ones (<see cref="AppModelCatalog.Models"/>, always, unchanged) and those
/// the repository's model catalog added (0.1.26, checked by the desktop app's remote catalog reader and added once at start, before anything
/// reads the list). An added model carries the digest Ollama must report for its manifest after the pull (<see cref="ManifestDigest"/>); a
/// tag that exists already is never replaced, so the built-in list is the floor.
/// </summary>
public static class AppModelSet
{
    private static readonly Lock Gate = new();
    private static volatile IReadOnlyList<AppModelInfo> _models = AppModelCatalog.Models;
    private static volatile IReadOnlyDictionary<string, string> _digests = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The built-in models, then the added ones.</summary>
    public static IReadOnlyList<AppModelInfo> Models => _models;

    /// <summary>
    /// Adds the models whose tag is new, each with the SHA-256 digest of its manifest (<c>sha256:…</c>, as Ollama lists it); returns how many
    /// were added.
    /// </summary>
    public static int Add(IEnumerable<AppModelInfo> models, IReadOnlyDictionary<string, string> digests)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(digests);
        lock (Gate)
        {
            var added = models.Where(m => _models.All(e => !string.Equals(e.Tag, m.Tag, StringComparison.Ordinal)) && digests.ContainsKey(m.Tag))
                .DistinctBy(m => m.Tag).ToList();
            if (added.Count > 0)
            {
                var known = new Dictionary<string, string>(_digests, StringComparer.Ordinal);
                foreach (var model in added)
                {
                    known[model.Tag] = digests[model.Tag];
                }

                _digests = known;
                _models = [.. _models, .. added];
            }

            return added.Count;
        }
    }

    /// <summary>The manifest digest an added model must have after its pull (<c>sha256:…</c>); null for a built-in or unknown tag.</summary>
    public static string? ManifestDigest(string tag) => _digests.TryGetValue(tag, out var digest) ? digest : null;
}

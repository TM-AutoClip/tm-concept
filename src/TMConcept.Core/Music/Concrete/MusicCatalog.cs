using System.Text.Json;
using System.Text.RegularExpressions;
using TMConcept.Core.Render;

namespace TMConcept.Core.Music;

/// <summary>
/// The ready library's catalog (0.1.45, C4): <c>catalog/music.json</c> of the repository, embedded in the app. A small, hand-picked list of
/// Kevin MacLeod's Incompetech tracks (CC BY 4.0): title, page, file address, licence, attribution, the app's mood and style tags, tempo,
/// length, size and the SHA-256 of the public file. The files are not in the app: they are downloaded as one component after the user agreed.
/// </summary>
public static partial class MusicCatalog
{
    private static readonly Lazy<MusicCatalogFile> Embedded = new(Load);

    /// <summary>The embedded catalog.</summary>
    public static MusicCatalogFile Shipped => Embedded.Value;

    /// <summary>Reads a catalog; <see cref="InvalidDataException"/> when it cannot be read or its format is not this app's.</summary>
    public static MusicCatalogFile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        MusicCatalogFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, CoreJsonContext.Default.MusicCatalogFile);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The music catalog cannot be read: " + ex.Message, ex);
        }

        if (file is null || file.Schema != MusicLibraryRules.CatalogSchema)
        {
            throw new InvalidDataException("The music catalog's format is not " + MusicLibraryRules.CatalogSchema + ".");
        }

        return file;
    }

    /// <summary>
    /// What is wrong with a catalog (empty = usable): a version that is not a folder name, a doubled id, a file address off
    /// <see cref="MusicLibraryRules.Host"/> or not HTTPS, a SHA-256 that is not 64 hex digits, a file name that is not a plain name, another
    /// licence than CC BY 4.0, no attribution, a tag outside <see cref="MusicTags"/>.
    /// </summary>
    public static IReadOnlyList<string> Problems(MusicCatalogFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var problems = new List<string>();
        if (!SafeName().IsMatch(file.Version))
        {
            problems.Add("version: not a folder name");
        }

        foreach (var group in file.Tracks.GroupBy(t => t.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add(group.Key + ": the id is used twice");
        }

        foreach (var track in file.Tracks)
        {
            var id = track.Id.Length > 0 ? track.Id : "(no id)";
            if (!Uri.TryCreate(track.DownloadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || url.Host != MusicLibraryRules.Host)
            {
                problems.Add(id + ": the file is not on https://" + MusicLibraryRules.Host);
            }

            if (!Uri.TryCreate(track.PageUrl, UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps)
            {
                problems.Add(id + ": no page");
            }

            if (!Sha256().IsMatch(track.Sha256))
            {
                problems.Add(id + ": no SHA-256");
            }

            if (!SafeName().IsMatch(track.File) || track.Bytes <= 0)
            {
                problems.Add(id + ": no file name or size");
            }

            if (track.License != MusicLibraryRules.License || track.Attribution.Trim().Length == 0)
            {
                problems.Add(id + ": not CC BY 4.0 with its attribution");
            }

            if (track.Moods.Count == 0 || track.Styles.Count == 0 || track.Moods.Concat(track.Styles).Any(t => !MusicTags.Moods.Contains(t) && !MusicTags.Styles.Contains(t)))
            {
                problems.Add(id + ": a tag outside the app's words");
            }
        }

        return problems;
    }

    private static MusicCatalogFile Load()
    {
        using var stream = typeof(MusicCatalog).Assembly.GetManifestResourceStream(MusicLibraryRules.CatalogResource)
            ?? throw new InvalidOperationException("The music catalog is not in the app.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,79}$")]
    private static partial Regex SafeName();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}

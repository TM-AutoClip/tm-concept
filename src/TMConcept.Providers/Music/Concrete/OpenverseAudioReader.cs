using System.Globalization;
using System.Text.Json.Nodes;
using TMConcept.Core.Music;

namespace TMConcept.Providers.Music;

/// <summary>
/// Reads an Openverse audio search answer (0.1.45, S6; pure, tests read recorded answers): every result whose licence is CC0, CC BY or CC
/// BY-SA (<see cref="MusicLicenses.Allowed"/>; a non-commercial, no-derivatives or unknown licence is dropped and counted) with an HTTPS file
/// address becomes a <see cref="MusicTrack"/> with its page, creator, licence and the attribution Openverse wrote.
/// </summary>
public static class OpenverseAudioReader
{
    /// <summary>The page of <paramref name="json"/>; <see cref="InvalidDataException"/> when it is not an Openverse answer.</summary>
    public static MusicSearchPage Read(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("The search answer is not an object.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException("The search answer cannot be read: " + ex.Message, ex);
        }

        if (root["results"] is not JsonArray results)
        {
            throw new InvalidDataException("The search answer has no results.");
        }

        var tracks = new List<MusicTrack>();
        var dropped = 0;
        foreach (var item in results.OfType<JsonObject>())
        {
            if (Track(item) is { } track)
            {
                tracks.Add(track);
            }
            else
            {
                dropped++;
            }
        }

        var page = (int)(Number(root["page"]) ?? 1);
        var pages = (int)(Number(root["page_count"]) ?? page);
        return new MusicSearchPage(tracks, Math.Max(1, page), Math.Max(1, pages), dropped);
    }

    /// <summary>One result as a track; <c>null</c> when its licence is not allowed or it has no usable file address.</summary>
    public static MusicTrack? Track(JsonObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var license = Text(item["license"]);
        var id = Text(item["id"]);
        var url = Text(item["url"]);
        if (!MusicLicenses.Allowed(license) || id.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var label = MusicLicenses.Label(license, Text(item["license_version"]));
        var title = Text(item["title"]);
        var creator = Text(item["creator"]);
        var page = Text(item["foreign_landing_url"]);
        var attribution = Text(item["attribution"]);
        if (attribution.Length == 0 && MusicLicenses.AsksCredit(label))
        {
            attribution = string.Join(" · ", new[] { title.Length > 0 ? "\"" + title + "\"" : string.Empty, creator, label, page }.Where(p => p.Length > 0));
        }

        return new MusicTrack
        {
            Id = MusicLibraryRules.OpenversePrefix + id,
            Title = title.Length > 0 ? title : id,
            Artist = creator,
            PageUrl = page.Length > 0 ? page : Text(item["detail_url"]),
            DownloadUrl = address.ToString(),
            License = label,
            Attribution = MusicLicenses.AsksCredit(label) ? attribution : string.Empty,
            DurationSeconds = (int)Math.Round((Number(item["duration"]) ?? 0) / 1000),
            Bytes = (long)(Number(item["filesize"]) ?? 0),
        };
    }

    /// <summary>The search address of one request (<paramref name="licenses"/>: <c>license_type=commercial,modification</c> or <c>license=cc0</c>).</summary>
    public static Uri Address(string query, int page, string licenses)
    {
        ArgumentNullException.ThrowIfNull(query);
        var clean = query.Trim();
        if (clean.Length > OpenverseRules.MaxQuery)
        {
            clean = clean[..OpenverseRules.MaxQuery];
        }

        return new Uri(OpenverseRules.SearchAddress + "?q=" + Uri.EscapeDataString(clean) + "&" + licenses + "&page_size="
            + OpenverseRules.PageSize.ToString(CultureInfo.InvariantCulture) + "&page=" + Math.Max(1, page).ToString(CultureInfo.InvariantCulture));
    }

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;
}

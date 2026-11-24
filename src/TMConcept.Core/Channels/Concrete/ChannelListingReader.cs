using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TMConcept.Core.Concepts;
using TMConcept.Core.References;

namespace TMConcept.Core.Channels;

/// <summary>
/// Reads yt-dlp's <c>-J --flat-playlist</c> answer about a channel (0.1.53; untrusted data from the web, only read): the channel's name
/// (<c>channel</c>, <c>uploader</c>, else the title without its " - Videos" tail), its description, its photo (the <c>avatar_uncropped</c>
/// thumbnail, else the largest square one; https only, never the banner), its videos in order (nested lists are walked, at most the asked
/// number) and the tabs a channel's home lists instead of videos (<c>ie_key</c> YoutubeTab, or a list without its own entries). A video needs
/// an id and an http(s) address without a user name; a live or upcoming one is marked. Texts are cleaned and cut. Pure.
/// </summary>
public static class ChannelListingReader
{
    private static readonly string[] TabTails = [" - Videos", " - Shorts", " - Live", " - Home", " - Videolar", " - Canlı"];

    /// <summary>The channel of <paramref name="json"/> with at most <paramref name="count"/> videos; throws <see cref="ChannelInspireException"/> ("list") when it is not a JSON object.</summary>
    public static ChannelListing Read(string json, int count)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json ?? string.Empty) as JsonObject ?? throw new ChannelInspireException("yt-dlp's answer is not a JSON object.") { Code = "list" };
        }
        catch (JsonException ex)
        {
            throw new ChannelInspireException("yt-dlp's answer could not be read.", ex) { Code = "list" };
        }

        var videos = new List<ChannelVideo>();
        var tabs = new List<string>();
        Walk(root["entries"] as JsonArray, Math.Max(0, count), videos, tabs, depth: 0);
        return new ChannelListing(Name(root), PromptText.Data(Text(root, "description"), ChannelLimits.ChannelDescriptionMax), Photo(root), videos, tabs);
    }

    /// <summary>The first line of yt-dlp's output that is a JSON object (its warnings and errors come on the same output), or null.</summary>
    public static string? JsonLine(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.FirstOrDefault(l => l.TrimStart().StartsWith('{'));
    }

    private static void Walk(JsonArray? entries, int count, List<ChannelVideo> videos, List<string> tabs, int depth)
    {
        if (entries is null || depth > 3)
        {
            return;
        }

        foreach (var entry in entries.OfType<JsonObject>())
        {
            if (videos.Count >= count)
            {
                return;
            }

            if (entry["entries"] is JsonArray nested)
            {
                Walk(nested, count, videos, tabs, depth + 1);
                continue;
            }

            var url = Text(entry, "url") ?? Text(entry, "webpage_url");
            if (Text(entry, "ie_key") == "YoutubeTab" || Text(entry, "_type") == "playlist")
            {
                if (ReferenceMeasurer.Address(url) is { } tab && tabs.Count < ChannelLimits.TabsMax * 2)
                {
                    tabs.Add(tab.AbsoluteUri);
                }

                continue;
            }

            var id = Text(entry, "id");
            if (string.IsNullOrWhiteSpace(id) || ReferenceMeasurer.Address(url) is not { } address)
            {
                continue;
            }

            var live = Text(entry, "live_status") is "is_live" or "is_upcoming" || entry["is_live"] is JsonValue v && v.TryGetValue<bool>(out var isLive) && isLive;
            videos.Add(new ChannelVideo(id.Trim(), address.AbsoluteUri, PromptText.Line(Text(entry, "title"), ChannelLimits.TitleMax), Seconds(entry),
                PromptText.Line(Text(entry, "description"), ChannelLimits.DescriptionMax), Uploaded(entry), Number(entry, "width"), Number(entry, "height"), live));
        }
    }

    private static string Name(JsonObject root)
    {
        var name = Text(root, "channel") ?? Text(root, "uploader") ?? Text(root, "playlist_uploader");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Text(root, "title") ?? string.Empty;
            foreach (var tail in TabTails)
            {
                if (name.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^tail.Length];
                    break;
                }
            }
        }

        return PromptText.Line(name, ChannelLimits.NameMax);
    }

    private static string? Photo(JsonObject root)
    {
        if (root["thumbnails"] is not JsonArray thumbnails)
        {
            return null;
        }

        var items = thumbnails.OfType<JsonObject>()
            .Select(t => (Id: Text(t, "id") ?? string.Empty, Url: Text(t, "url"), Width: Number(t, "width"), Height: Number(t, "height")))
            .Where(t => Uri.TryCreate(t.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
                && !t.Id.Contains("banner", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var avatar = items.FirstOrDefault(t => t.Id.Equals("avatar_uncropped", StringComparison.OrdinalIgnoreCase));
        if (avatar.Url is not null)
        {
            return avatar.Url;
        }

        return items.Where(t => t.Width > 0 && t.Width == t.Height).OrderByDescending(t => t.Width).Select(t => t.Url).FirstOrDefault();
    }

    private static double? Seconds(JsonObject entry) =>
        entry["duration"] is JsonValue value && value.TryGetValue<double>(out var seconds) && seconds > 0 && double.IsFinite(seconds) ? seconds : null;

    private static DateOnly? Uploaded(JsonObject entry)
    {
        if (DateOnly.TryParseExact(Text(entry, "upload_date"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        foreach (var key in new[] { "timestamp", "release_timestamp" })
        {
            if (entry[key] is JsonValue value && value.TryGetValue<double>(out var unix) && unix > 0 && unix < 32_503_680_000)
            {
                return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds((long)unix).UtcDateTime);
            }
        }

        return null;
    }

    private static int Number(JsonObject item, string key) =>
        item[key] is JsonValue value && value.TryGetValue<double>(out var number) && number is > 0 and < 100_000 ? (int)number : 0;

    private static string? Text(JsonObject item, string key) =>
        item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

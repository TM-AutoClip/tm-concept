using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using TMConcept.Core.Render;

namespace TMConcept.Core.Videos;

/// <summary>
/// The asset record of a version folder (0.1.44, F15; <see cref="VideoFolders.AssetsFile"/>, next to <see cref="VersionFolders"/>): which
/// outside files the video uses, with their source, licence and attribution, so the credit a track asks for is in one place (H2: the video
/// page shows it after the video, ready to copy into the description; it is never drawn into the video).
/// </summary>
public static class AssetLedger
{
    // Readable by the user: indented, and names with letters outside ASCII ("Gymnopédie", "Şarkı") kept as they are.
    private static readonly CoreJsonContext Indented = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    });

    /// <summary>Writes the record into <paramref name="versionFolder"/> (an empty list when the video uses nothing from outside).</summary>
    public static async Task WriteAsync(string versionFolder, IReadOnlyList<AssetEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(versionFolder);
        ArgumentNullException.ThrowIfNull(entries);
        var json = JsonSerializer.Serialize(entries.ToList(), Indented.ListAssetEntry);
        await File.WriteAllTextAsync(Path.Combine(versionFolder, VideoFolders.AssetsFile), json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The record of a version folder; empty when it has none (made before 0.1.44) or it cannot be read.</summary>
    public static async Task<IReadOnlyList<AssetEntry>> ReadAsync(string versionFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(versionFolder);
        var file = Path.Combine(versionFolder, VideoFolders.AssetsFile);
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, CoreJsonContext.Default.ListAssetEntry) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The entry asks for credit: a Creative Commons Attribution licence (CC BY and its variants; CC0 does not), or an attribution text the
    /// user wrote for their own file.
    /// </summary>
    public static bool NeedsAttribution(AssetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Attribution.Trim().Length > 0
            || entry.License.Trim().Replace('-', ' ').StartsWith(AssetLicenses.CcByPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The credit lines for the video's description, one per entry that asks for it (its own attribution text, or for a CC BY entry without
    /// one: name, artist, licence and source); empty when none asks.
    /// </summary>
    public static string AttributionText(IEnumerable<AssetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var lines = entries.Where(NeedsAttribution).Select(Line).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Line(AssetEntry entry)
    {
        if (entry.Attribution.Trim() is { Length: > 0 } own)
        {
            return own;
        }

        var name = entry.Artist.Trim().Length > 0 ? $"\"{entry.Name.Trim()}\" — {entry.Artist.Trim()}" : $"\"{entry.Name.Trim()}\"";
        var parts = new List<string> { name, entry.License.Trim() };
        if (entry.SourceUrl.Trim() is { Length: > 0 } url)
        {
            parts.Add(url);
        }

        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }
}

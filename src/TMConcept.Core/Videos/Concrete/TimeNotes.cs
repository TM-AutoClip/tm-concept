using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using TMConcept.Core.Render;

namespace TMConcept.Core.Videos;

/// <summary>
/// The notes on a made video (F16, 0.1.49): a moment of the video belongs to the scene playing then (the version's own timing, its
/// <c>source/input.json</c>), and the notes of a version are a JSON file in its folder (<see cref="VideoFolders.NotesFile"/>; readable,
/// indented; the database is not changed).
/// </summary>
public static class TimeNotes
{
    private static readonly CoreJsonContext Indented = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    });

    /// <summary>
    /// The scene of <paramref name="input"/> playing at <paramref name="atMs"/> (1-based): the last scene that starts at or before it; before the
    /// first scene the first, past the end the last. 0 when the input has no scene.
    /// </summary>
    public static int SceneAt(RenderInput input, long atMs)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Scenes.Count == 0)
        {
            return 0;
        }

        var seconds = Math.Max(0, atMs) / 1000.0;
        var scene = input.Scenes[0];
        foreach (var candidate in input.Scenes.OrderBy(s => s.StartSeconds))
        {
            if (candidate.StartSeconds <= seconds + 0.0005)
            {
                scene = candidate;
            }
        }

        return scene.Number;
    }

    /// <summary>The notes grouped by scene, in scene order, each scene's notes in time order (the "Fix" request).</summary>
    public static IReadOnlyList<SceneNote> ByScene(IEnumerable<TimeNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return notes.Where(n => n.Scene > 0 && n.Text.Trim().Length > 0).GroupBy(n => n.Scene).OrderBy(g => g.Key)
            .Select(g => new SceneNote(g.Key, string.Join(" / ", g.OrderBy(n => n.AtMs).Select(n => n.Text.Trim())))).ToList();
    }

    /// <summary>Writes the notes of a version (an empty list removes the file).</summary>
    public static async Task WriteAsync(string versionFolder, IReadOnlyList<TimeNote> notes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(versionFolder);
        ArgumentNullException.ThrowIfNull(notes);
        var file = Path.Combine(versionFolder, VideoFolders.NotesFile);
        if (notes.Count == 0)
        {
            File.Delete(file);
            return;
        }

        var json = JsonSerializer.Serialize(notes.OrderBy(n => n.AtMs).ToList(), Indented.ListTimeNote);
        await File.WriteAllTextAsync(file, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The notes of a version; empty when it has none or the file cannot be read.</summary>
    public static async Task<IReadOnlyList<TimeNote>> ReadAsync(string versionFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(versionFolder);
        var file = Path.Combine(versionFolder, VideoFolders.NotesFile);
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), CoreJsonContext.Default.ListTimeNote) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

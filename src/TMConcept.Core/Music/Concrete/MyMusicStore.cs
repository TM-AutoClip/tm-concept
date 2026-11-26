using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Media;
using TMConcept.Core.Paths;
using TMConcept.Core.Render;
using TMConcept.Core.Videos;

namespace TMConcept.Core.Music;

/// <summary>
/// <see cref="IMyMusic"/> in <c>&lt;data&gt;/my-music</c> (0.1.54): every piece as <c>&lt;guid&gt;.&lt;ext&gt;</c>, the list in
/// <see cref="MusicLibraryRules.MineIndex"/> (written to a temporary file first, then moved over the old one). One change at a time. A piece
/// whose file is gone is left out of the list. Its length is read with ffprobe when ffmpeg is installed (0 otherwise; the video reads it
/// anyway). An added file's licence is the user's (<see cref="AssetLicenses.OwnFile"/>); a made piece's is its model's
/// (<see cref="AssetLicenses.GeneratedPrefix"/>), so a video plays both from their start.
/// </summary>
public sealed class MyMusicStore(IAppPaths paths, IMediaTools media, IFolderDiscard discard, ILogger<MyMusicStore> logger) : IMyMusic, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public string Folder => Path.Combine(paths.DataDirectory, MusicLibraryRules.MineFolder);

    private string Index => Path.Combine(Folder, MusicLibraryRules.MineIndex);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MyMusicEntry>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Where(e => File.Exists(e.Track.File))
                .OrderByDescending(e => e.AddedAtUtc).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<MyMusicEntry> AddFileAsync(string file, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (!File.Exists(file))
        {
            throw new FileNotFoundException("The music file was not found.", Path.GetFileName(file));
        }

        var entry = new MyMusicEntry
        {
            Track = new MusicTrack
            {
                Title = Path.GetFileNameWithoutExtension(file),
                License = AssetLicenses.OwnFile,
            },
        };
        await StoreAsync(entry, file, move: false, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("My music: {Name} added from a file", entry.Track.Title);
        return entry;
    }

    /// <inheritdoc />
    public async Task<MyMusicEntry> AddGeneratedAsync(GeneratedPiece piece, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentException.ThrowIfNullOrEmpty(piece.File);
        var entry = new MyMusicEntry
        {
            Generated = true,
            Model = piece.Model,
            Prompt = piece.Prompt,
            ReferenceName = piece.ReferenceName,
            Usd = piece.Usd,
            Track = new MusicTrack
            {
                Title = piece.Title.Trim().Length > 0 ? piece.Title.Trim() : Path.GetFileNameWithoutExtension(piece.File),
                Artist = piece.Model,
                License = AssetLicenses.GeneratedPrefix + piece.Model,
                Bpm = Math.Max(0, piece.Bpm),
            },
        };
        await StoreAsync(entry, piece.File, move: true, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("My music: a piece made by {Model} added ({Usd} USD)", piece.Model, piece.Usd);
        return entry;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var entry = all.FirstOrDefault(e => e.Track.Id == id);
            if (entry is null)
            {
                return;
            }

            if (File.Exists(entry.Track.File) && LibraryPaths.Inside(entry.Track.File, Folder))
            {
                await discard.DiscardAsync(entry.Track.File, cancellationToken).ConfigureAwait(false);
            }

            all.Remove(entry);
            await WriteAsync(all, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("My music: {Name} removed", entry.Track.Title);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task StoreAsync(MyMusicEntry entry, string file, bool move, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, id + Path.GetExtension(file).ToLowerInvariant());
        if (move)
        {
            File.Move(file, target);
        }
        else
        {
            await LibraryPaths.CopyAsync(file, target, FileMode.CreateNew, cancellationToken).ConfigureAwait(false);
        }

        entry.Track.Id = MusicLibraryRules.MinePrefix + id;
        entry.Track.File = target;
        entry.Track.Bytes = new FileInfo(target).Length;
        entry.Track.DurationSeconds = await SecondsAsync(target, cancellationToken).ConfigureAwait(false);
        entry.AddedAtUtc = DateTimeOffset.UtcNow;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(cancellationToken).ConfigureAwait(false);
            all.Add(entry);
            await WriteAsync(all, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> SecondsAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            return await media.AudioDurationAsync(file, cancellationToken).ConfigureAwait(false) is { } ms ? (int)Math.Round(ms / 1000.0) : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // ffmpeg is not installed yet: the length is read when a video uses the piece.
            logger.LogInformation("My music: the length of a piece could not be read now ({Error})", ex.Message);
            return 0;
        }
    }

    private async Task<List<MyMusicEntry>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Index))
        {
            return [];
        }

        try
        {
            var text = await File.ReadAllTextAsync(Index, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(text, CoreJsonContext.Default.ListMyMusicEntry) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "My music: the list could not be read; it starts empty (the files stay)");
            return [];
        }
    }

    private async Task WriteAsync(List<MyMusicEntry> all, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Folder);
        var temporary = Index + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(all, CoreJsonContext.Default.ListMyMusicEntry), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, Index, overwrite: true);
    }
}

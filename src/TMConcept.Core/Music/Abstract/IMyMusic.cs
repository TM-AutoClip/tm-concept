namespace TMConcept.Core.Music;

/// <summary>
/// The user's own music collection (0.1.54, "Müziklerim"): pieces made on the "Müzik üret" page and audio files the user added. Each file is a
/// copy in <c>&lt;data&gt;/my-music</c> (the original may move); the list is a JSON file next to them. A video's music choice can take any of
/// them (<see cref="Styles.MusicSource.MyMusic"/>). Nothing here uses the network.
/// </summary>
public interface IMyMusic
{
    /// <summary>The collection's folder.</summary>
    string Folder { get; }

    /// <summary>Every piece, newest first.</summary>
    Task<IReadOnlyList<MyMusicEntry>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Copies the user's audio file into the collection.</summary>
    Task<MyMusicEntry> AddFileAsync(string file, CancellationToken cancellationToken);

    /// <summary>Moves a made piece into the collection with its model, prompt, reference and cost.</summary>
    Task<MyMusicEntry> AddGeneratedAsync(GeneratedPiece piece, CancellationToken cancellationToken);

    /// <summary>Removes a piece from the list and deletes its file for good (the caller asked first).</summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken);
}

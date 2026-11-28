namespace TMConcept.Core.Music;

/// <summary>
/// Makes one piece of background music from the user's own prompt (0.1.54, the "Müzik üret" page) with the music models the videos use
/// (<see cref="IMusicGenerators"/>: the local ACE-Step 1.5 under the graphics card lock, or an online model) and puts it into "Müziklerim"
/// (<see cref="IMyMusic"/>). With a reference sound its tempo (beats per minute, measured by the app) goes into the request: none of the
/// models takes a sound as input. Failures throw <see cref="MusicGenerationException"/>.
/// </summary>
public interface IMusicMaker
{
    /// <summary>What a piece of <paramref name="seconds"/> of <paramref name="model"/> costs (0 for the local model) and the length really asked.</summary>
    (decimal Usd, int Seconds) Estimate(Models.CatalogModel model, int seconds);

    /// <summary>The work folders of the pieces being made now (the start-up sweep never deletes them; every other one is a closed app's leftover).</summary>
    IReadOnlyCollection<string> ActiveFolders { get; }

    /// <summary>Makes the piece and adds it to the collection; <paramref name="progress"/> gets the stage ("reference", "generate", "save").</summary>
    Task<MyMusicEntry> MakeAsync(MusicMakeRequest request, IProgress<string>? progress, CancellationToken cancellationToken);
}

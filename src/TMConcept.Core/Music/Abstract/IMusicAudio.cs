namespace TMConcept.Core.Music;

/// <summary>The sound work of the music analysis (0.1.45, F7 + F8), done by ffmpeg on the processor.</summary>
public interface IMusicAudio
{
    /// <summary>
    /// The first <paramref name="maxSeconds"/> of <paramref name="file"/> as mono samples at <paramref name="sampleRate"/>; <c>null</c> when
    /// ffmpeg cannot read it. <paramref name="work"/> is a folder for the raw samples.
    /// </summary>
    Task<float[]?> ReadMonoAsync(string file, int sampleRate, int maxSeconds, string work, CancellationToken cancellationToken);

    /// <summary>Writes the track laid out by <paramref name="bed"/> (entry, loop copies, cross-fades) to <paramref name="output"/>; false when ffmpeg failed.</summary>
    Task<bool> RenderBedAsync(string file, MusicBed bed, string output, CancellationToken cancellationToken);
}

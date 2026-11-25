namespace TMConcept.Desktop.ViewModels;

/// <summary>A piece of "Müziklerim" on the music page (0.1.54): the piece and its line (where it came from, length, tempo, cost, prompt).</summary>
public sealed record MyMusicRow(Core.Music.MyMusicEntry Entry, string Detail)
{
    /// <summary>Its name.</summary>
    public string Title => Entry.Track.Title;
}

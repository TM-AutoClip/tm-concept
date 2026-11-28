using TMConcept.Core.Music;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// How a music model's progress reads on screen (0.1.61, plan K14/K15), the same on the music page and in the video's step row: "Aşama 2/3 ·
/// blok 16/40 · %40" / "Stage 2/3 · block 16/40 · 40 %"; a stage without a count shows only its stage (and "çözülüyor" for the decoding); a
/// model that counts nothing (an online model, ACE-Step) shows nothing but the time so far.
/// </summary>
public interface IMusicProgressText
{
    /// <summary>The progress line ("Aşama 2/3 · blok 16/40 · %40"; empty for <see cref="MusicProgress.Unknown"/>).</summary>
    string Line(MusicProgress progress);

    /// <summary>The line with the time so far ("Aşama 2/3 · blok 16/40 · %40 · 3:12"; "geçen süre 0:42" when nothing is counted).</summary>
    string WithTime(MusicProgress progress, TimeSpan elapsed);

    /// <summary>The video step row's line ("Müzik: Aşama 2/3 · blok 16/40 · %40"; "Müzik üretiliyor" when nothing is counted).</summary>
    string Video(MusicProgress progress);
}

using TMConcept.Core.Models;
using TMConcept.Core.Music;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// How a music model and a piece of "Müziklerim" read on screen (0.1.55; the music page and the video page's music choice each had their own
/// copy): a model's label with its price or install state and, for a model that needs a graphics card, its card note; the yellow note of the
/// chosen model on this computer; a piece's detail line.
/// </summary>
public interface IMusicChoiceText
{
    /// <summary>
    /// "ACE-Step 1.5 · not installed yet: downloaded when you start (NVIDIA only)" / "Eleven Music — 0.15 $ a minute". <paramref name="ready"/>
    /// = installed (an online model always is).
    /// </summary>
    string ModelLabel(CatalogModel model, bool ready, LocalMusicCard card);

    /// <summary>The yellow note of <paramref name="model"/> on this computer (a card it is not made for, Linux); empty when there is nothing to say.</summary>
    string CardWarning(CatalogModel? model, LocalMusicCard card);

    /// <summary>A piece's detail line: made by which model or an own file, its length; <paramref name="full"/> adds its tempo, cost and prompt.</summary>
    string PieceDetail(MyMusicEntry entry, bool full);
}

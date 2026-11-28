using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.ViewModels;

/// <inheritdoc cref="IMusicChoiceText" />
public sealed class MusicChoiceText(Localizer text) : IMusicChoiceText
{
    /// <inheritdoc />
    public string ModelLabel(CatalogModel model, bool ready, LocalMusicCard card)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(card);
        if (model.Provider != ModelProviders.Local)
        {
            return model.MusicUsdPerPiece > 0
                ? text.Format("styles.music.generate.price.piece", model.Name, text.Usd(model.MusicUsdPerPiece))
                : text.Format("styles.music.generate.price.minute", model.Name, text.Usd(model.MusicUsdPerMinute));
        }

        // Like every other model list: a model not installed yet is chosen as it is and installed (after one confirmation) when it starts.
        var label = model.Name + (ready ? string.Empty : " · " + text["models.not_installed"]);
        if (LocalMusicSupport.NeedsCard(model))
        {
            // 28 Nov 2026: its card class like every other local model, red "this card cannot run it" above this card's class.
            label += " · " + text.Format("models.tier", model.MinVideoMemoryGb)
                + (LocalMusicSupport.TooBig(model, card) ? " · " + text["models.tier.too_big"] : string.Empty)
                + (card.NvidiaOnly ? " " + text["style.music.generatelocal.nvidia_only"] : string.Empty)
                + (card.Linux ? " " + text["style.music.generatelocal.unverified"] : string.Empty);
        }

        // The licence badge of the model pickers (orange for non-commercial, rule 19; asked once before the first use).
        label += model.LicenseUse switch
        {
            LicenseUse.NonCommercial => " · " + text["models.license.badge.noncommercial"],
            LicenseUse.Limited => " · " + text["models.license.badge.limited"],
            _ => string.Empty,
        };
        return label;
    }

    /// <inheritdoc />
    public string CardWarning(CatalogModel? model, LocalMusicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (model is null || !LocalMusicSupport.NeedsCard(model))
        {
            return string.Empty;
        }

        // Never a block (rule 19): the model is offered and tried; only said.
        return string.Join(" ", new[]
        {
            card.NvidiaOnly ? text["styles.music.generate.nvidia_warning"] : string.Empty,
            LocalMusicSupport.TooBig(model, card) ? text.Format("styles.music.generate.too_big", model.MinVideoMemoryGb, (int)card.Tier) : string.Empty,
            card.Linux ? text["styles.music.generate.linux"] : string.Empty,
        }.Where(p => p.Length > 0));
    }

    /// <inheritdoc />
    public string PieceDetail(MyMusicEntry entry, bool full)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var parts = new List<string> { entry.Generated ? text.Format("music.mine.generated", entry.Model) : text["music.mine.file"] };
        if (entry.Track.DurationSeconds > 0)
        {
            parts.Add(TimeSpan.FromSeconds(entry.Track.DurationSeconds).ToString(@"m\:ss", text.Culture));
        }

        if (!full)
        {
            return string.Join(" · ", parts);
        }

        if (entry.Track.Bpm > 0)
        {
            parts.Add(entry.Track.Bpm.ToString(text.Culture) + " BPM");
        }

        if (entry.Usd > 0)
        {
            parts.Add(text.Usd(entry.Usd));
        }

        if (entry.Prompt.Length > 0)
        {
            parts.Add("“" + entry.Prompt + "”");
        }

        return string.Join(" · ", parts);
    }
}

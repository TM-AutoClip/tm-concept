using TMConcept.Core.AppOllama;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Desktop.Localization;

namespace TMConcept.Desktop.Catalog;

/// <summary>
/// The notes behind the "?" of a model in a picker or a list (0.1.23, the developer): built ONLY from what the code knows — the catalog entry
/// (where it runs, price, context, licence, graphics card class, main memory, retirement date) and the <see cref="ModelFacts"/> of its engine
/// (languages, own voice samples, download size, this card). Nothing is judged ("best", "good quality"): an unknown fact is left out. The
/// list tip names the entries that share useful facts ("Turkish, commercial use and this card: …").
/// </summary>
public sealed class ModelNotes(Localizer text)
{
    /// <summary>The most names a line of the list tip shows (the rest as "+N").</summary>
    public const int MaxNames = 8;

    private const double Gib = 1024.0 * 1024 * 1024;

    /// <summary>The notes of <paramref name="model"/>, one fact per line.</summary>
    public IReadOnlyList<string> Lines(CatalogModel model, ModelFacts facts)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(facts);
        var lines = new List<string> { Where(model) };
        if (Price(model) is { Length: > 0 } price)
        {
            lines.Add(price);
        }

        if (facts.Languages is { Count: > 0 } languages)
        {
            lines.Add(languages.Contains(ContentLanguages.Turkish, StringComparer.Ordinal) ? text["notes.turkish.yes"] : text["notes.turkish.no"]);
            lines.Add(text.Format("notes.languages", string.Join(", ", languages.Select(l => OutputLanguages.NameIn(l, text.Language)))));
        }

        if (facts.CopiesVoice is { } copies)
        {
            lines.Add(text[copies ? "notes.voice.own" : "notes.voice.builtin"]);
        }

        if (model.License.Length > 0)
        {
            lines.Add(text.Format(model.LicenseUse switch
            {
                LicenseUse.NonCommercial => "notes.license.noncommercial",
                LicenseUse.Limited => "notes.license.limited",
                _ => "notes.license.commercial",
            }, LicenseName(model.License)));
        }

        if (model.MinVideoMemoryGb > 0)
        {
            lines.Add(text.Format("notes.card.class", model.MinVideoMemoryGb));
            if (Fits(model, facts) is { } fits)
            {
                lines.Add(fits
                    ? text.Format("notes.card.fits", Gigabytes(facts.CardBytes!.Value))
                    : text.Format("notes.card.too_big", Gigabytes(facts.CardBytes!.Value)));
            }
            else if (facts.NoCard)
            {
                lines.Add(text["notes.card.processor"]);
            }
        }

        if (model.MemoryGb > 0)
        {
            lines.Add(text.Format("notes.memory", model.MemoryGb));
        }

        if (facts.DownloadBytes is > 0 and var bytes)
        {
            lines.Add(text.Format("notes.download", Gigabytes(bytes)));
        }

        if (model.ContextTokens > 0)
        {
            lines.Add(text.Format("notes.context", Tokens(model.ContextTokens)));
        }

        if (facts.TrainedContextTokens > model.ContextTokens)
        {
            lines.Add(text.Format("notes.context.trained", Tokens(facts.TrainedContextTokens)));
        }

        if (model.MaxOutputTokens > 0)
        {
            lines.Add(text.Format("notes.max_output", Tokens(model.MaxOutputTokens)));
        }

        if (model.Capabilities.HasFlag(ModelCapability.Vision))
        {
            lines.Add(text["notes.vision"]);
        }

        if (model.Efforts.Count > 0)
        {
            lines.Add(text.Format("notes.efforts", string.Join(", ", model.Efforts)));
        }
        else if (facts.CanThink == true)
        {
            lines.Add(text["notes.thinks"]);
        }

        if (model.RetiresOn.Length > 0)
        {
            lines.Add(text.Format("notes.retires", model.RetiresOn));
        }

        return lines;
    }

    /// <summary>The notes of <paramref name="model"/> as one text (a line per fact).</summary>
    public string Text(CatalogModel model, ModelFacts facts) => string.Join('\n', Lines(model, facts));

    /// <summary>
    /// The notes of one of the app's own Ollama models on Settings → Local AI (its size, card fit and licence are on the row already): the
    /// context it is run with and was trained with, whether it reads images and whether it can think.
    /// </summary>
    public string Text(AppModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var lines = new List<string> { text["notes.where.app_ollama"] };
        if (model.ContextTokens > 0)
        {
            lines.Add(text.Format("notes.context", Tokens(model.ContextTokens)));
        }

        if (model.TrainedContextTokens > model.ContextTokens)
        {
            lines.Add(text.Format("notes.context.trained", Tokens(model.TrainedContextTokens)));
        }

        lines.Add(text[model.Vision ? "notes.vision" : "notes.vision.no"]);
        lines.Add(text[model.CanThink ? "notes.thinks" : "notes.thinks.no"]);
        if (model.License.Length > 0)
        {
            lines.Add(text.Format(model.LicenseUse switch
            {
                LicenseUse.NonCommercial => "notes.license.noncommercial",
                LicenseUse.Limited => "notes.license.limited",
                _ => "notes.license.commercial",
            }, LicenseName(model.License)));
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The tip over a list: which entries share useful facts — free without a key, speak <paramref name="language"/>, speak with your own
    /// voice samples, allow commercial use, run on this card, and those that do all of these at once. Empty when no line has a name.
    /// </summary>
    public string ListTip(IReadOnlyList<(CatalogModel Model, ModelFacts Facts)> entries, string language)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var lines = new List<string>();
        var languageName = OutputLanguages.NameIn(language, text.Language);
        var speaks = entries.Where(e => e.Facts.Languages?.Contains(language, StringComparer.Ordinal) == true).ToList();
        var commercial = entries.Where(e => e.Model.License.Length > 0 && e.Model.LicenseUse == LicenseUse.Commercial).ToList();
        var fits = entries.Where(e => e.Model.MinVideoMemoryGb > 0 && Fits(e.Model, e.Facts) == true).ToList();
        Add(lines, "notes.tip.free", entries.Where(e => IsFree(e.Model)));
        Add(lines, "notes.tip.speaks", speaks, languageName);
        Add(lines, "notes.tip.own_voice", entries.Where(e => e.Facts.CopiesVoice == true));
        Add(lines, "notes.tip.commercial", commercial);
        Add(lines, "notes.tip.fits", fits);
        var all = commercial.Intersect(fits).ToList();
        if (speaks.Count > 0)
        {
            Add(lines, "notes.tip.all.language", all.Intersect(speaks), languageName);
        }
        else
        {
            Add(lines, "notes.tip.all", all);
        }

        return string.Join('\n', lines);
    }

    /// <summary>Whether the card's class sees the model's class (true when it needs no card); null when the card is not known.</summary>
    public static bool? Fits(CatalogModel model, ModelFacts facts)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(facts);
        if (model.MinVideoMemoryGb <= 0)
        {
            return true;
        }

        return facts.CardBytes is > 0 and var bytes ? VramTiers.Sees(VramTiers.ForCard(bytes), VramTiers.ForModel(model.MinVideoMemoryGb)) : null;
    }

    private static bool IsFree(CatalogModel model) =>
        model.Provider is ModelProviders.Local or ModelProviders.AppOllama || EndpointCodes.IsEndpoint(model.Provider);

    private void Add(List<string> lines, string key, IEnumerable<(CatalogModel Model, ModelFacts Facts)> entries, string? language = null)
    {
        var names = entries.Select(e => e.Model.Name).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return;
        }

        var shown = string.Join(", ", names.Take(MaxNames)) + (names.Count > MaxNames ? " " + text.Format("notes.tip.more", names.Count - MaxNames) : string.Empty);
        lines.Add(language is null ? text.Format(key, shown) : text.Format(key, language, shown));
    }

    private string Where(CatalogModel model) =>
        model.Provider == ModelProviders.Local ? text["notes.where.local"]
        : model.Provider == ModelProviders.AppOllama ? text["notes.where.app_ollama"]
        : EndpointCodes.IsEndpoint(model.Provider) ? text["notes.where.endpoint"]
        : text.Format("notes.where.online", model.Provider);

    private string Price(CatalogModel model)
    {
        if (model.Capabilities.HasFlag(ModelCapability.Image) && model.ImageUsd > 0)
        {
            return text.Format("notes.price.image", text.Usd(model.ImageUsd));
        }

        if (model.Capabilities.HasFlag(ModelCapability.Speech) && model.SpeechUsdPerMillionCharacters > 0)
        {
            return text.Format("notes.price.speech", text.Usd(model.SpeechUsdPerMillionCharacters));
        }

        return model.InputUsd > 0 || model.OutputUsd > 0 ? text.Format("notes.price.text", text.Usd(model.InputUsd), text.Usd(model.OutputUsd)) : string.Empty;
    }

    private string LicenseName(string code) => text.Has("license." + code) ? text["license." + code] : code;

    private string Gigabytes(long bytes) => Math.Round(bytes / Gib, 1).ToString("0.#", text.Culture);

    private string Tokens(long tokens) => tokens.ToString("N0", text.Culture);
}

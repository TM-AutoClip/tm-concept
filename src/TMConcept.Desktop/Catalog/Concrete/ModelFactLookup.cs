using TMConcept.Core.AppOllama;
using TMConcept.Core.Models;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Engines.Images;
using TMConcept.Speech.Python;

namespace TMConcept.Desktop.Catalog;

/// <summary>
/// Gathers the <see cref="ModelFacts"/> of a catalog model from the code's own catalogs: the voice engine's languages, the Python voices'
/// own-voice support and pinned files, the local image models' pinned files and the app's Ollama catalog (size, trained context, thinking).
/// A model none of them knows gets no such facts.
/// </summary>
public static class ModelFactLookup
{
    /// <summary>
    /// The facts of <paramref name="model"/>; <paramref name="engine"/> is its voice engine (null for other models), <paramref name="cardBytes"/>
    /// the memory of the card it would run on (null = unknown or none) and <paramref name="noCard"/> whether there is no card it can use.
    /// </summary>
    public static ModelFacts For(CatalogModel model, ISpeechEngine? engine, long? cardBytes, bool noCard)
    {
        ArgumentNullException.ThrowIfNull(model);
        var app = model.Provider == ModelProviders.AppOllama ? AppModelAdvisor.Find(model.Id) : null;
        var voice = model.Provider == ModelProviders.Local ? PythonVoices.All.FirstOrDefault(v => v.Code == model.Id) : null;
        return new ModelFacts
        {
            Languages = engine is null ? null : ContentLanguages.All.Where(engine.Supports).ToList(),
            CopiesVoice = voice?.CopiesVoice,
            DownloadBytes = DownloadBytes(model, voice) ?? app?.Bytes,
            CardBytes = cardBytes,
            NoCard = noCard,
            TrainedContextTokens = app?.TrainedContextTokens ?? 0,
            CanThink = app?.CanThink,
        };
    }

    private static long? DownloadBytes(CatalogModel model, PythonVoice? voice)
    {
        if (model.Provider != ModelProviders.Local)
        {
            return null;
        }

        if (voice is not null)
        {
            // A voice built on another one (Ahenk on VoxCPM2) needs that one's files too.
            var files = voice.Files.Sum(f => f.ApproximateBytes);
            var baseVoice = voice.BaseVoice is { } code ? PythonVoices.All.FirstOrDefault(v => v.Code == code) : null;
            return files + (baseVoice?.Files.Sum(f => f.ApproximateBytes) ?? 0);
        }

        return LocalImageModels.All.FirstOrDefault(m => m.Id == model.Id && model.Capabilities.HasFlag(ModelCapability.Image))?.TotalBytes;
    }
}

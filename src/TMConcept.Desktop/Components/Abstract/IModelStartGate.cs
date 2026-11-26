using TMConcept.Core.Models;
using TMConcept.Core.Tools;

namespace TMConcept.Desktop.Components;

/// <summary>
/// What every page does before a chosen model runs (0.1.55; it was inside the model pickers, the music page had its own "download first"
/// way): the model's licence when it is not plainly commercial (asked once per model), a working API key for an online model (the key window
/// opens; only a key that works is kept), then ONE confirmation that lists every download of the models and of the extra components (ffmpeg…),
/// and the install. Anything declined or failed: not ready, with the reason; nothing was started, downloaded or paid for.
/// </summary>
public interface IModelStartGate
{
    /// <summary>Asks for <paramref name="model"/>'s licence when it is not plainly commercial and was not accepted before; false when declined.</summary>
    Task<bool> AcceptLicenseAsync(CatalogModel model);

    /// <summary>Every online provider of <paramref name="models"/> gets a working key (asked in the key window); the first one left without.</summary>
    Task<KeyGateOutcome> KeysAsync(IReadOnlyList<CatalogModel> models, CancellationToken cancellationToken);

    /// <summary>
    /// The licences and the keys of <paramref name="models"/>, without installing anything (a page that lists the downloads in a confirmation of
    /// its own with other things, such as a video's).
    /// </summary>
    Task<ModelStartOutcome> AllowAsync(IReadOnlyList<CatalogModel> models, CancellationToken cancellationToken);

    /// <summary>
    /// The components <paramref name="codes"/> in one confirmation and their install, once the licences and the keys were asked (the model
    /// pickers ask those per row, then call this; <see cref="PrepareAsync"/> calls it too). Nothing to install: ready at once.
    /// </summary>
    Task<ModelStartOutcome> InstallAsync(IReadOnlyCollection<string> codes, string purposeKey, IProgress<ToolProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// The licences, the keys, then the components of <paramref name="models"/> and <paramref name="components"/> in one confirmation
    /// (<paramref name="purposeKey"/> says what waits for them).
    /// </summary>
    Task<ModelStartOutcome> PrepareAsync(IReadOnlyList<CatalogModel> models, IReadOnlyList<string> components, string purposeKey,
        IProgress<ToolProgress>? progress, CancellationToken cancellationToken);
}

using TMConcept.Core.Plans;
using TMConcept.Core.Render;

namespace TMConcept.Core.Videos;

/// <summary>
/// The model calls of the quality modes (F11 + F14, 0.1.50). Every call is paid through <see cref="QualityCall.Steps"/> (a local model at no
/// cost) and counted also when its answer cannot be read. A review that cannot be read returns null (the rounds stop; the video is kept).
/// </summary>
public interface IQualityReviewer
{
    /// <summary>The frame review: one call with the sheet of <paramref name="frames"/>; every finding, or null.</summary>
    Task<IReadOnlyList<QualityFinding>?> FramesAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames, bool subtitles,
        CancellationToken cancellationToken);

    /// <summary>The picture review (careful mode, pictures made on this computer): one call with every picture; every finding, or null.</summary>
    Task<IReadOnlyList<QualityFinding>?> PicturesAsync(QualityCall call, IReadOnlyList<QualityPicture> pictures, int scenes, CancellationToken cancellationToken);

    /// <summary>The character comparison (F14, careful mode): one call with the sheet and the scene pictures; the scenes that differ, or null.</summary>
    Task<IReadOnlyList<QualityFinding>?> CharacterAsync(QualityCall call, string sheet, string character, IReadOnlyList<QualityPicture> pictures, int scenes,
        CancellationToken cancellationToken);

    /// <summary>The final critic: one call with the sheet; the scores of the headings it answered (empty: none could be read).</summary>
    Task<IReadOnlyList<CriticScore>> CriticAsync(QualityCall call, string sheet, RenderInput input, IReadOnlyList<QualityFrame> frames, bool subtitles,
        CancellationToken cancellationToken);

    /// <summary>The repair of one scene's plan (careful mode, the app's drawer): one small plan call; the scene's new plan, or null.</summary>
    Task<PlanItem?> RepairAsync(QualityRepairRequest request, CancellationToken cancellationToken);
}

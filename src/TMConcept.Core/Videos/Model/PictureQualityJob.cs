using TMConcept.Core.Models;

namespace TMConcept.Core.Videos;

/// <summary>
/// The careful mode's look at the scene pictures (F11 + F14, 0.1.50; <see cref="QualityStep.PicturesAsync"/>): the reviewer, whether the
/// pictures are reviewed and made again in rounds (<paramref name="Loop"/>: both the image model and the reviewer run on this computer, the
/// TODO item of 16 Nov 2026), the pictures with their prompts, the scene count, the recurring character (empty: none), what draws the
/// character sheet (null: no sheet; returns its file or null), what makes the faulty pictures again (returns their scenes), the run's
/// progress, the video's id and the report it fills.
/// </summary>
public sealed record PictureQualityJob(ResolvedModel Reviewer, bool Loop, IReadOnlyList<QualityPicture> Pictures, int Scenes, string Character,
    Func<CancellationToken, Task<string?>>? CharacterSheet, Func<IReadOnlyList<QualityFinding>, CancellationToken, Task<IReadOnlyList<int>>> Redraw,
    VideoStepProgress Steps, Guid VideoId, QualityReport Report);

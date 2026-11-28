using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>
/// One catalog model of a local music family (28 Nov 2026: a checkpoint of one of <see cref="LocalMusicFamilies.All"/>). Its files are pinned downloads whose
/// <see cref="ToolArchive.ExecutablePath"/> is their path inside the family's model folder: its own files under <see cref="Folder"/>, files it
/// shares with other models of its family (MAGNeT's T5 text encoder) under <see cref="SharedFolder"/>, downloaded once for all of them.
/// </summary>
/// <param name="Id">Its catalog id (<c>local.json</c>; its weights component is <c>music.&lt;id&gt;</c>).</param>
/// <param name="Folder">Its own folder inside the family's model folder.</param>
/// <param name="MaxSeconds">
/// The longest piece it makes. Only a model that makes exactly this length (MAGNeT, DiffRhythm's base models, DiffRhythm 1.0 Full) has the
/// same <c>musicSeconds</c> in the catalog; for the others the asked length is cut to this. The pages ask at most
/// <see cref="Core.Music.MusicGenerationRules.MaxSeconds"/>, so the catalog's names promise no more than that.
/// </param>
/// <param name="Files">Every file it needs, pinned by commit, SHA-256 and size.</param>
/// <param name="SharedFolder">The folder of the files it shares with other models of its family; empty when none.</param>
/// <param name="RunOptions">The arguments of its own its family's script reads (<see cref="LocalMusicOption"/>); null when none.</param>
public sealed record LocalMusicVariant(string Id, string Folder, int MaxSeconds, IReadOnlyList<ToolArchive> Files, string SharedFolder = "",
    IReadOnlyList<LocalMusicOption>? RunOptions = null)
{
    /// <summary>The arguments of its own its family's script reads (empty when none).</summary>
    public IReadOnlyList<LocalMusicOption> Options => RunOptions ?? [];
}

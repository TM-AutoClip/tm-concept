using TMConcept.Core.Tools;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// A family of local music models that run the same way (28 Nov 2026: every family of <see cref="LocalMusicFamilies.All"/>, MusicGen to YuE): one Python environment of the app's
/// own (a lock shipped with the app, <see cref="PythonEnvironment"/>), one script shipped with the app that makes one piece and writes a
/// WAV, optionally a source archive the script runs from (MAGNeT: audiocraft; DiffRhythm, SongGeneration, YuE2, YuE: their own code; never installed as a package), and its catalog models.
/// </summary>
/// <param name="Key">Its key (the folder under <c>&lt;tools&gt;/music-models</c>).</param>
/// <param name="Name">Its name in the logs and the install window.</param>
/// <param name="Environment">Its Python environment.</param>
/// <param name="Script">Its script (one of the app's Python files, <see cref="PythonFiles"/>).</param>
/// <param name="Variants">Its catalog models.</param>
/// <param name="Source">The source archive its script runs from, pinned by SHA-256; null when none.</param>
/// <param name="SourceLicense">The licence of its source, named in the install window (audiocraft: MIT).</param>
/// <param name="SourceName">
/// The name of its source component (<c>music-code.&lt;name&gt;</c>); empty = from the archive's root folder (audiocraft). DiffRhythm 1.0 and
/// 1.2 run from two commits of the same repository, so each names its own.
/// </param>
/// <param name="Folder">
/// The folder under <c>&lt;tools&gt;/music-models</c> its models live in; empty = its key. Two families that share files (DiffRhythm 1.0 and
/// 1.2: the VAE and MuQ-MuLan) share a folder; each keeps its own source in it (<see cref="LocalMusicLayout.SourceFolder"/>).
/// </param>
/// <param name="Lyrics">
/// Its script takes the words to sing (<c>--lyrics-file</c>, an empty file for an instrumental piece; 28 Nov 2026: SongGeneration, YuE).
/// </param>
public sealed record LocalMusicFamily(string Key, string Name, PythonEnvironment Environment, string Script, IReadOnlyList<LocalMusicVariant> Variants,
    ToolArchive? Source = null, string SourceLicense = "MIT", string SourceName = "", string Folder = "", bool Lyrics = false)
{
    /// <summary>The folder under <c>&lt;tools&gt;/music-models</c> its models live in.</summary>
    public string FolderName => Folder.Length == 0 ? Key : Folder;
}

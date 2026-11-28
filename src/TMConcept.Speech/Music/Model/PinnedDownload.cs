using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>One pinned download of a local music model's install (28 Nov 2026): the file (its address, SHA-256 and size) and where it goes.</summary>
/// <param name="File">The file (<see cref="ToolArchive.ExecutablePath"/> names it in the progress line).</param>
/// <param name="Target">Its full path on this computer.</param>
public sealed record PinnedDownload(ToolArchive File, string Target);

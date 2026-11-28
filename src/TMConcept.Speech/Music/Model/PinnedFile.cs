namespace TMConcept.Speech.Music;

/// <summary>One file of a Hugging Face repository at a pinned commit (28 Nov 2026, the local music models): its path there, SHA-256 and size.</summary>
/// <param name="Path">Its path in the repository.</param>
/// <param name="Sha256">Its SHA-256 (the Git LFS object id of a large file, measured for a small one).</param>
/// <param name="Bytes">Its size.</param>
public sealed record PinnedFile(string Path, string Sha256, long Bytes);

using TMConcept.Core.Tools;

namespace TMConcept.Speech.Music;

/// <summary>The pinned downloads of the local music models' weights (28 Nov 2026): only https addresses of Hugging Face at a commit.</summary>
public static class LocalMusicFiles
{
    /// <summary>
    /// The downloads of <paramref name="files"/> of the Hugging Face repository <paramref name="repository"/> at <paramref name="commit"/> (40
    /// lower-case hex characters, never a branch), each saved as <paramref name="folder"/>/its path inside the family's model folder.
    /// </summary>
    public static IReadOnlyList<ToolArchive> From(string repository, string commit, string folder, IReadOnlyList<PinnedFile> files)
    {
        ArgumentException.ThrowIfNullOrEmpty(repository);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(files);
        if (commit is not { Length: 40 } || !commit.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("A Hugging Face file is pinned to a commit (40 lower-case hex characters), not to " + commit, nameof(commit));
        }

        return files.Select(f =>
        {
            if (f.Sha256 is not { Length: 64 } || !f.Sha256.All(char.IsAsciiHexDigitLower) || f.Bytes <= 0)
            {
                throw new ArgumentException("A pinned file needs its SHA-256 (64 lower-case hex characters) and its size: " + f.Path, nameof(files));
            }

            return new ToolArchive(ToolKind.MusicWeights, commit[..7], "any", new Uri("https://huggingface.co/" + repository + "/resolve/" + commit + "/" + f.Path),
                f.Sha256, string.Empty, folder + "/" + f.Path, f.Bytes);
        }).ToList();
    }
}
